using System.Text.Json;
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
            var tags = BuildTags(op, category);

            var descriptor = new McpToolDescriptor
            {
                Name = toolName,
                Description = description,
                Category = category,
                Tags = tags,
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

    private static IReadOnlyList<string> BuildTags(CanonicalOperation op, string? category)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            op.OperationType.ToString().ToLowerInvariant()
        };

        if (!string.IsNullOrWhiteSpace(category))
        {
            tags.Add(category.ToLowerInvariant());
        }

        return tags.OrderBy(tag => tag, StringComparer.Ordinal).ToArray();
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
}
