using System.Text.Json;
using System.Text.RegularExpressions;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using GraphQL.MCP.Abstractions.Policy;
using GraphQL.MCP.Core.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GraphQL.MCP.Core.Publishing;

/// <summary>
/// Transforms canonical GraphQL operations into MCP tool descriptors.
/// Generates JSON Schema for inputs and constructs GraphQL query strings.
/// </summary>
public sealed class ToolPublisher
{
    private readonly IMcpPolicy _policy;
    private readonly McpOptions _options;
    private readonly ILogger<ToolPublisher> _logger;

    public ToolPublisher(
        IMcpPolicy policy,
        IOptions<McpOptions> options,
        ILogger<ToolPublisher> logger)
    {
        _policy = policy;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generates MCP tool descriptors from a set of canonical operations.
    /// </summary>
    public IReadOnlyList<McpToolDescriptor> Publish(IReadOnlyList<CanonicalOperation> operations)
    {
        using var activity = McpActivitySource.Source.StartActivity("mcp.publish");
        activity?.SetTag("mcp.publish.input_count", operations.Count);

        var tools = new List<McpToolDescriptor>();
        var publishedNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var op in operations)
        {
            var toolName = _policy.TransformToolName(op);
            if (publishedNames.TryGetValue(toolName, out var existingField))
            {
                throw new InvalidOperationException(
                    $"Multiple GraphQL operations map to the same MCP tool name '{toolName}': " +
                    $"'{existingField}' and '{op.GraphQLFieldName}'. " +
                    "Adjust ToolPrefix/NamingPolicy or exclude one of the operations.");
            }

            var inputSchema = BuildInputSchema(op);
            var graphqlQuery = BuildGraphQLQuery(op);
            var argumentMapping = BuildArgumentMapping(op);

            var description = BuildDescription(op);
            var category = InferCategory(op);
            var domain = InferDomain(op, category);
            var tags = BuildTags(op, category, domain);
            var semanticHints = BuildSemanticHints(op, category, domain);

            var descriptor = new McpToolDescriptor
            {
                Name = toolName,
                Description = description,
                Category = category,
                Domain = domain,
                Tags = tags,
                SemanticHints = semanticHints,
                InputSchema = inputSchema,
                GraphQLQuery = graphqlQuery,
                OperationType = op.OperationType,
                GraphQLFieldName = op.GraphQLFieldName,
                ArgumentMapping = argumentMapping
            };

            tools.Add(descriptor);
            publishedNames[toolName] = op.GraphQLFieldName;
            _logger.LogDebug(
                "Published tool '{ToolName}' from GraphQL field '{Field}' ({OpType})",
                toolName, op.GraphQLFieldName, op.OperationType);
        }

        activity?.SetTag("mcp.publish.output_count", tools.Count);
        McpActivitySource.PublishedToolCount.Add(tools.Count);
        _logger.LogInformation("Published {Count} MCP tools", tools.Count);
        return tools;
    }

    private string BuildDescription(CanonicalOperation op)
    {
        var prefix = op.OperationType == OperationType.Mutation ? "[MUTATION] " : "";
        var desc = _options.IncludeDescriptions && !string.IsNullOrWhiteSpace(op.Description)
            ? op.Description
            : $"GraphQL {op.OperationType.ToString().ToLowerInvariant()} operation: {op.GraphQLFieldName}";
        return $"{prefix}{desc}";
    }

    private JsonDocument BuildInputSchema(CanonicalOperation op)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WritePropertyName("properties");
        writer.WriteStartObject();

        var requiredArgs = new List<string>();

        foreach (var arg in op.Arguments)
        {
            writer.WritePropertyName(arg.Name);
            WriteTypeSchema(writer, arg.Type, arg.Description, arg.DefaultValue);

            if (arg.IsRequired)
            {
                requiredArgs.Add(arg.Name);
            }
        }

        writer.WriteEndObject(); // properties

        if (requiredArgs.Count > 0)
        {
            writer.WritePropertyName("required");
            writer.WriteStartArray();
            foreach (var name in requiredArgs)
            {
                writer.WriteStringValue(name);
            }
            writer.WriteEndArray();
        }

        writer.WriteBoolean("additionalProperties", false);
        writer.WriteEndObject();
        writer.Flush();

        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }

    private void WriteTypeSchema(
        Utf8JsonWriter writer,
        CanonicalType type,
        string? description = null,
        object? defaultValue = null)
    {
        writer.WriteStartObject();

        // Unwrap NonNull
        var inner = type;
        if (inner.Kind == TypeKind.NonNull && inner.OfType is not null)
        {
            inner = inner.OfType;
        }

        // Write description if available
        if (_options.IncludeDescriptions && !string.IsNullOrWhiteSpace(description))
        {
            writer.WriteString("description", description);
        }

        switch (inner.Kind)
        {
            case TypeKind.Scalar:
                var jsonType = MapScalarToJsonType(inner.Name);
                writer.WriteString("type", jsonType);
                break;

            case TypeKind.Enum:
                writer.WriteString("type", "string");
                if (inner.EnumValues is { Count: > 0 })
                {
                    writer.WritePropertyName("enum");
                    writer.WriteStartArray();
                    foreach (var val in inner.EnumValues)
                    {
                        writer.WriteStringValue(val);
                    }
                    writer.WriteEndArray();
                }
                break;

            case TypeKind.List:
                writer.WriteString("type", "array");
                if (inner.OfType is not null)
                {
                    writer.WritePropertyName("items");
                    WriteTypeSchema(writer, inner.OfType);
                }
                break;

            case TypeKind.InputObject:
                writer.WriteString("type", "object");
                if (inner.Fields is { Count: > 0 })
                {
                    writer.WritePropertyName("properties");
                    writer.WriteStartObject();
                    var requiredFields = new List<string>();
                    foreach (var field in inner.Fields)
                    {
                        writer.WritePropertyName(field.Name);
                        WriteTypeSchema(writer, field.Type, field.Description);

                        // Detect NonNull fields in input objects as required
                        if (field.Type.Kind == TypeKind.NonNull || field.Type.IsNonNull)
                        {
                            requiredFields.Add(field.Name);
                        }
                    }
                    writer.WriteEndObject();

                    if (requiredFields.Count > 0)
                    {
                        writer.WritePropertyName("required");
                        writer.WriteStartArray();
                        foreach (var name in requiredFields)
                        {
                            writer.WriteStringValue(name);
                        }
                        writer.WriteEndArray();
                    }
                }
                break;

            default:
                writer.WriteString("type", "object");
                break;
        }

        // Write default value if available
        if (defaultValue is not null)
        {
            WriteDefaultValue(writer, defaultValue);
        }

        writer.WriteEndObject();
    }

    private static void WriteDefaultValue(Utf8JsonWriter writer, object value)
    {
        writer.WritePropertyName("default");
        switch (value)
        {
            case string s:
                writer.WriteStringValue(s);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static string MapScalarToJsonType(string graphqlType) => graphqlType switch
    {
        "String" or "ID" => "string",
        "Int" => "integer",
        "Float" => "number",
        "Boolean" => "boolean",
        _ => "string" // custom scalars → string
    };

    private static string? InferCategory(CanonicalOperation op)
    {
        var returnType = GetInnermostType(op.ReturnType);
        if (returnType.Kind is TypeKind.Object or TypeKind.Interface or TypeKind.Union)
        {
            return returnType.Name;
        }

        return op.OperationType == OperationType.Query ? "Query" : "Mutation";
    }

    private static string InferDomain(CanonicalOperation op, string? category)
    {
        if (!string.IsNullOrWhiteSpace(category) &&
            !category.Equals("Query", StringComparison.OrdinalIgnoreCase) &&
            !category.Equals("Mutation", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDomainName(category);
        }

        return NormalizeDomainName(op.GraphQLFieldName);
    }

    private static IReadOnlyList<string> BuildTags(CanonicalOperation op, string? category, string? domain)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            op.OperationType.ToString().ToLowerInvariant()
        };

        if (!string.IsNullOrWhiteSpace(category))
        {
            tags.Add(category.ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            tags.Add(domain);
        }

        return tags.OrderBy(tag => tag, StringComparer.Ordinal).ToArray();
    }

    private static McpSemanticHints BuildSemanticHints(CanonicalOperation op, string? category, string domain)
    {
        var intent = InferIntent(op);
        var keywords = BuildKeywords(op, category, domain);

        return new McpSemanticHints
        {
            Intent = intent,
            Keywords = keywords
        };
    }

    private static string InferIntent(CanonicalOperation op)
    {
        var action = GetActionWord(op);
        return action switch
        {
            "list" => "list",
            "search" or "find" => "search",
            "count" => "count",
            "create" or "add" or "new" => "create",
            "update" or "set" or "modify" or "patch" or "edit" => "update",
            "delete" or "remove" or "archive" or "drop" => "delete",
            "upsert" or "replace" => "upsert",
            "fetch" or "get" or "lookup" or "read" or "show" or "load" or "view" or "return" => "retrieve",
            _ => op.OperationType == OperationType.Query ? "retrieve" : "write"
        };
    }

    private static IReadOnlyList<string> BuildKeywords(CanonicalOperation op, string? category, string domain)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            op.OperationType.ToString().ToLowerInvariant()
        };

        AddNormalizedKeywords(keywords, op.GraphQLFieldName);
        AddNormalizedKeywords(keywords, op.Name);
        AddNormalizedKeywords(keywords, category);
        AddNormalizedKeywords(keywords, domain);
        AddNormalizedKeywords(keywords, op.Description);

        foreach (var argument in op.Arguments)
        {
            AddNormalizedKeywords(keywords, argument.Name);
            AddNormalizedKeywords(keywords, argument.Description);
        }

        return keywords.OrderBy(keyword => keyword, StringComparer.Ordinal).ToArray();
    }

    private static void AddNormalizedKeywords(HashSet<string> keywords, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var token in NormalizeTokens(value, stripLeadingNoise: false, stripTrailingStructural: false))
        {
            keywords.Add(token);
        }
    }

    private static CanonicalType GetInnermostType(CanonicalType type)
    {
        var current = type;
        while (current.OfType is not null)
        {
            current = current.OfType;
        }

        return current;
    }

    private static string NormalizeDomainName(string value)
    {
        var tokens = NormalizeTokens(value, stripLeadingNoise: true, stripTrailingStructural: true);
        if (tokens.Count == 0)
        {
            return "general";
        }

        foreach (var token in tokens)
        {
            if (!GenericDomainTokens.Contains(token))
            {
                return token;
            }
        }

        return "general";
    }

    private static List<string> NormalizeTokens(
        string value,
        bool stripLeadingNoise,
        bool stripTrailingStructural)
    {
        var tokens = SplitIdentifier(value);
        if (tokens.Count == 0)
        {
            return [];
        }

        tokens = MergeCompoundTokens(tokens);
        var normalized = tokens.Select(SingularizeToken).ToList();

        if (stripLeadingNoise)
        {
            while (normalized.Count > 0 &&
                (NoiseTokens.Contains(normalized[0]) || VerbPrefixes.Contains(normalized[0])))
            {
                normalized.RemoveAt(0);
            }
        }

        if (stripTrailingStructural)
        {
            while (normalized.Count > 0 && StructuralSuffixTokens.Contains(normalized[^1]))
            {
                normalized.RemoveAt(normalized.Count - 1);
            }
        }

        return normalized;
    }

    private static List<string> SplitIdentifier(string value)
    {
        var matches = IdentifierTokenRegex().Matches(value);
        if (matches.Count == 0)
        {
            return [];
        }

        var tokens = new List<string>(matches.Count);
        foreach (Match match in matches)
        {
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                tokens.Add(match.Value);
            }
        }

        return tokens;
    }

    private static List<string> MergeCompoundTokens(List<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return tokens;
        }

        var merged = new List<string>(tokens.Count);
        for (var index = 0; index < tokens.Count; index++)
        {
            var current = tokens[index];
            if (index < tokens.Count - 1 &&
                CompoundTokenSuffixes.TryGetValue(current, out var suffixes) &&
                suffixes.Contains(tokens[index + 1]))
            {
                merged.Add(current + tokens[index + 1]);
                index++;
                continue;
            }

            merged.Add(current);
        }

        return merged;
    }

    private static string SingularizeToken(string token)
    {
        var lower = token.ToLowerInvariant();

        if (lower.Length <= 3)
        {
            return lower;
        }

        if (IrregularSingulars.TryGetValue(lower, out var singular))
        {
            return singular;
        }

        if (UnsingularizableTokens.Contains(lower))
        {
            return lower;
        }

        if (lower.EndsWith("ies", StringComparison.Ordinal) && lower.Length > 3)
        {
            return lower[..^3] + "y";
        }

        if (lower.EndsWith("ches", StringComparison.Ordinal) ||
            lower.EndsWith("shes", StringComparison.Ordinal) ||
            lower.EndsWith("xes", StringComparison.Ordinal) ||
            lower.EndsWith("zes", StringComparison.Ordinal) ||
            lower.EndsWith("ses", StringComparison.Ordinal))
        {
            return lower[..^2];
        }

        if (lower.EndsWith("s", StringComparison.Ordinal) &&
            !lower.EndsWith("ss", StringComparison.Ordinal) &&
            !lower.EndsWith("us", StringComparison.Ordinal) &&
            !lower.EndsWith("is", StringComparison.Ordinal) &&
            !lower.EndsWith("ics", StringComparison.Ordinal))
        {
            return lower[..^1];
        }

        return lower;
    }

    private static string GetActionWord(CanonicalOperation op)
    {
        var tokens = SplitIdentifier(op.GraphQLFieldName);
        if (tokens.Count > 0)
        {
            var first = tokens[0];
            if (QueryActionMap.TryGetValue(first, out var mappedQueryAction) &&
                op.OperationType == OperationType.Query)
            {
                return mappedQueryAction;
            }

            if (MutationActionWords.Contains(first))
            {
                return first.ToLowerInvariant();
            }
        }

        if (!string.IsNullOrWhiteSpace(op.Description))
        {
            var descriptionTokens = SplitIdentifier(op.Description);
            if (descriptionTokens.Count > 0)
            {
                var firstDescriptionToken = descriptionTokens[0];
                if (QueryActionMap.TryGetValue(firstDescriptionToken, out var mappedDescriptionAction) &&
                    op.OperationType == OperationType.Query)
                {
                    return mappedDescriptionAction;
                }

                if (MutationActionWords.Contains(firstDescriptionToken))
                {
                    return firstDescriptionToken.ToLowerInvariant();
                }
            }
        }

        return op.OperationType == OperationType.Query ? "fetch" : "modify";
    }

    private string BuildGraphQLQuery(CanonicalOperation op)
    {
        var operationKeyword = op.OperationType == OperationType.Query ? "query" : "mutation";
        var variableDecls = BuildVariableDeclarations(op.Arguments);
        var variableArgs = BuildVariableArguments(op.Arguments);
        var selectionSet = BuildSelectionSet(op.ReturnType, _policy.GetMaxDepth(), []);

        var varDeclStr = variableDecls.Length > 0 ? $"({variableDecls})" : "";
        var varArgStr = variableArgs.Length > 0 ? $"({variableArgs})" : "";

        return $"{operationKeyword} McpOperation{varDeclStr} {{ {op.GraphQLFieldName}{varArgStr}{selectionSet} }}";
    }

    private static string BuildVariableDeclarations(IReadOnlyList<CanonicalArgument> args)
    {
        if (args.Count == 0) return "";
        return string.Join(", ", args.Select(a => $"${a.Name}: {ToGraphQLTypeString(a.Type)}"));
    }

    private static string BuildVariableArguments(IReadOnlyList<CanonicalArgument> args)
    {
        if (args.Count == 0) return "";
        return string.Join(", ", args.Select(a => $"{a.Name}: ${a.Name}"));
    }

    private static string ToGraphQLTypeString(CanonicalType type)
    {
        if (type.Kind == TypeKind.NonNull && type.OfType is not null)
        {
            return $"{ToGraphQLTypeString(type.OfType)}!";
        }
        if (type.Kind == TypeKind.List && type.OfType is not null)
        {
            return $"[{ToGraphQLTypeString(type.OfType)}]";
        }
        return type.Name;
    }

    private string BuildSelectionSet(CanonicalType type, int depth, HashSet<string> visited)
    {
        if (depth <= 0) return "";

        // Unwrap NonNull/List wrappers
        var inner = type;
        while (inner.Kind is TypeKind.NonNull or TypeKind.List && inner.OfType is not null)
        {
            inner = inner.OfType;
        }

        switch (inner.Kind)
        {
            case TypeKind.Scalar or TypeKind.Enum:
                return ""; // scalars don't need selection sets

            case TypeKind.Object:
                if (visited.Contains(inner.Name))
                    return ""; // circular reference protection
                if (inner.Fields is null or { Count: 0 })
                    return "";

                var visitedCopy = new HashSet<string>(visited) { inner.Name };
                var fields = new List<string>();

                foreach (var field in inner.Fields)
                {
                    // Skip fields excluded by policy (e.g., ExcludedFields: ["internalNotes"])
                    if (_policy.IsFieldExcluded(field.Name))
                        continue;

                    var innerField = field.Type;
                    while (innerField.Kind is TypeKind.NonNull or TypeKind.List && innerField.OfType is not null)
                    {
                        innerField = innerField.OfType;
                    }

                    if (innerField.Kind is TypeKind.Scalar or TypeKind.Enum)
                    {
                        fields.Add(field.Name);
                    }
                    else if (depth > 1)
                    {
                        var nested = BuildSelectionSet(field.Type, depth - 1, visitedCopy);
                        if (!string.IsNullOrEmpty(nested))
                        {
                            fields.Add($"{field.Name}{nested}");
                        }
                    }
                }

                return fields.Count > 0 ? $" {{ {string.Join(" ", fields)} }}" : "";

            case TypeKind.Interface or TypeKind.Union:
                if (inner.PossibleTypes is null or { Count: 0 })
                    return " { __typename }";

                var fragments = new List<string> { "__typename" };
                foreach (var possibleType in inner.PossibleTypes)
                {
                    var fragment = BuildSelectionSet(possibleType, depth - 1, visited);
                    if (!string.IsNullOrEmpty(fragment))
                    {
                        fragments.Add($"... on {possibleType.Name}{fragment}");
                    }
                }
                return $" {{ {string.Join(" ", fragments)} }}";

            default:
                return "";
        }
    }

    private static IReadOnlyDictionary<string, string> BuildArgumentMapping(CanonicalOperation op)
    {
        return op.Arguments.ToDictionary(a => a.Name, a => a.Name);
    }

    private static readonly HashSet<string> VerbPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "get",
        "list",
        "fetch",
        "find",
        "search",
        "create",
        "update",
        "delete",
        "remove",
        "add",
        "set",
        "upsert",
        "patch",
        "put",
        "replace",
        "count"
    };

    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "all",
        "any",
        "each",
        "every",
        "the",
        "a",
        "an",
        "and",
        "or",
        "by",
        "for",
        "from",
        "in",
        "of",
        "on",
        "to",
        "with",
        "via",
        "query",
        "mutation"
    };

    private static readonly HashSet<string> GenericDomainTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "api",
        "apis",
        "graphql",
        "platform",
        "service",
        "services",
        "system",
        "systems"
    };

    private static readonly HashSet<string> StructuralSuffixTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "collection",
        "connection",
        "data",
        "edge",
        "edges",
        "item",
        "items",
        "list",
        "node",
        "nodes",
        "page",
        "pages",
        "payload",
        "payloads",
        "record",
        "records",
        "response",
        "responses",
        "result",
        "results",
        "resource",
        "resources",
        "viewer"
    };

    private static readonly HashSet<string> MutationActionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "add",
        "create",
        "delete",
        "patch",
        "put",
        "remove",
        "replace",
        "set",
        "update",
        "upsert"
    };

    private static readonly Dictionary<string, string> QueryActionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fetch"] = "fetch",
        ["find"] = "search",
        ["get"] = "fetch",
        ["list"] = "list",
        ["search"] = "search",
        ["count"] = "count"
    };

    private static readonly Dictionary<string, HashSet<string>> CompoundTokenSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Graph"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "QL" },
        ["Open"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "API" }
    };

    private static readonly Dictionary<string, string> IrregularSingulars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["children"] = "child",
        ["data"] = "data",
        ["feet"] = "foot",
        ["geese"] = "goose",
        ["indices"] = "index",
        ["men"] = "man",
        ["people"] = "person",
        ["series"] = "series",
        ["species"] = "species",
        ["teeth"] = "tooth",
        ["women"] = "woman"
    };

    private static readonly HashSet<string> UnsingularizableTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "analysis",
        "business",
        "status"
    };

    private static readonly Regex IdentifierTokenPattern =
        new(@"[A-Z]+(?=$|[A-Z][a-z]|\d)|[A-Z]?[a-z]+|\d+", RegexOptions.Compiled);

    private static Regex IdentifierTokenRegex() => IdentifierTokenPattern;
}
