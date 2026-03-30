using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using GraphQL.MCP.Abstractions.Policy;
using GraphQL.MCP.Core.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GraphQL.MCP.Core.Policy;

/// <summary>
/// Evaluates operations against configured policies to determine
/// which operations to publish and how to name them.
/// </summary>
public sealed partial class PolicyEngine : IMcpPolicy
{
    private readonly McpOptions _options;
    private readonly ILogger<PolicyEngine> _logger;
    private readonly Regex[]? _excludedFieldPatterns;
    private readonly Regex[]? _includedFieldPatterns;

    public PolicyEngine(IOptions<McpOptions> options, ILogger<PolicyEngine> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Pre-compile glob patterns for ExcludedFields
        _excludedFieldPatterns = _options.ExcludedFields.Count > 0
            ? _options.ExcludedFields.Select(GlobToRegex).ToArray()
            : null;

        // Pre-compile glob patterns for IncludedFields
        _includedFieldPatterns = _options.IncludedFields.Count > 0
            ? _options.IncludedFields.Select(GlobToRegex).ToArray()
            : null;
    }

    /// <inheritdoc />
    public bool ShouldIncludeOperation(CanonicalOperation operation)
    {
        // Exclude introspection fields
        if (operation.GraphQLFieldName.StartsWith("__", StringComparison.Ordinal))
        {
            _logger.LogDebug("Excluding introspection field: {Field}", operation.GraphQLFieldName);
            return false;
        }

        // Exclude mutations unless explicitly allowed
        if (operation.OperationType == OperationType.Mutation && !_options.AllowMutations)
        {
            _logger.LogInformation(
                "Excluding mutation '{Field}' — AllowMutations is false",
                operation.GraphQLFieldName);
            return false;
        }

        if (_options.RequireDescriptionsForPublishedTools &&
            string.IsNullOrWhiteSpace(operation.Description))
        {
            _logger.LogInformation(
                "Excluding field '{Field}' - description is required",
                operation.GraphQLFieldName);
            return false;
        }

        if (_options.MinDescriptionLength > 0 &&
            !string.IsNullOrWhiteSpace(operation.Description) &&
            operation.Description.Trim().Length < _options.MinDescriptionLength)
        {
            _logger.LogInformation(
                "Excluding field '{Field}' - description length {Length} is below MinDescriptionLength {Min}",
                operation.GraphQLFieldName,
                operation.Description.Trim().Length,
                _options.MinDescriptionLength);
            return false;
        }

        if (operation.Arguments.Count > _options.MaxArgumentCount)
        {
            _logger.LogInformation(
                "Excluding field '{Field}' - argument count {Count} exceeds MaxArgumentCount {Max}",
                operation.GraphQLFieldName,
                operation.Arguments.Count,
                _options.MaxArgumentCount);
            return false;
        }

        var argumentComplexity = CalculateArgumentComplexity(operation.Arguments);
        if (argumentComplexity > _options.MaxArgumentComplexity)
        {
            _logger.LogInformation(
                "Excluding field '{Field}' - argument complexity {Complexity} exceeds MaxArgumentComplexity {Max}",
                operation.GraphQLFieldName,
                argumentComplexity,
                _options.MaxArgumentComplexity);
            return false;
        }

        // Include allowlist check: when IncludedFields is set, only matching fields pass
        if (_includedFieldPatterns is not null)
        {
            if (!MatchesAnyPattern(operation.GraphQLFieldName, _includedFieldPatterns))
            {
                _logger.LogInformation(
                    "Excluding field '{Field}' — not in IncludedFields allowlist",
                    operation.GraphQLFieldName);
                return false;
            }
        }

        // Exclude by field name (supports glob patterns)
        if (_excludedFieldPatterns is not null &&
            MatchesAnyPattern(operation.GraphQLFieldName, _excludedFieldPatterns))
        {
            _logger.LogInformation(
                "Excluding field '{Field}' — matches ExcludedFields pattern",
                operation.GraphQLFieldName);
            return false;
        }

        var domain = InferDomain(operation);
        if (_options.IncludedDomains.Count > 0 &&
            !_options.IncludedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Excluding field '{Field}' - domain '{Domain}' is not in IncludedDomains",
                operation.GraphQLFieldName,
                domain);
            return false;
        }

        if (_options.ExcludedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Excluding field '{Field}' - domain '{Domain}' is in ExcludedDomains",
                operation.GraphQLFieldName,
                domain);
            return false;
        }

        // Exclude by return type (unwrap NonNull/List wrappers)
        var returnTypeName = GetInnerTypeName(operation.ReturnType);
        if (_options.ExcludedTypes.Contains(returnTypeName))
        {
            _logger.LogInformation(
                "Excluding field '{Field}' — return type '{Type}' is in ExcludedTypes",
                operation.GraphQLFieldName, returnTypeName);
            return false;
        }

        // Check arguments for excluded types
        foreach (var arg in operation.Arguments)
        {
            var innerTypeName = GetInnerTypeName(arg.Type);
            if (_options.ExcludedTypes.Contains(innerTypeName))
            {
                _logger.LogInformation(
                    "Excluding field '{Field}' — argument '{Arg}' uses excluded type '{Type}'",
                    operation.GraphQLFieldName, arg.Name, innerTypeName);
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public string TransformToolName(CanonicalOperation operation)
    {
        var baseName = _options.NamingPolicy switch
        {
            ToolNamingPolicy.VerbNoun => operation.OperationType == OperationType.Query
                ? $"get_{operation.GraphQLFieldName}"
                : operation.GraphQLFieldName,
            ToolNamingPolicy.Raw => operation.GraphQLFieldName,
            ToolNamingPolicy.PrefixedRaw => operation.GraphQLFieldName,
            _ => operation.GraphQLFieldName
        };

        // Apply prefix
        if (!string.IsNullOrEmpty(_options.ToolPrefix))
        {
            baseName = $"{_options.ToolPrefix}_{baseName}";
        }

        // Sanitize: replace non-alphanumeric (except _) with underscore
        baseName = SanitizeRegex().Replace(baseName, "_");

        // Collapse consecutive underscores
        baseName = CollapseUnderscoresRegex().Replace(baseName, "_");

        // Trim leading/trailing underscores
        baseName = baseName.Trim('_');

        // Enforce max length
        if (baseName.Length > 64)
        {
            baseName = baseName[..55] + "_" + CreateStableHashSuffix(baseName);
        }

        return baseName;
    }

    /// <inheritdoc />
    public int GetMaxDepth() => _options.MaxOutputDepth;

    /// <inheritdoc />
    public int GetMaxToolCount() => _options.MaxToolCount;

    /// <inheritdoc />
    public bool IsFieldExcluded(string fieldName)
    {
        if (_excludedFieldPatterns is not null && MatchesAnyPattern(fieldName, _excludedFieldPatterns))
            return true;

        return false;
    }

    /// <summary>
    /// Filters and transforms a list of operations according to policy.
    /// Returns only the operations that pass all policy checks, up to MaxToolCount.
    /// </summary>
    public IReadOnlyList<CanonicalOperation> Apply(IEnumerable<CanonicalOperation> operations)
    {
        using var activity = McpActivitySource.Source.StartActivity("mcp.policy.apply");

        var included = operations
            .Where(ShouldIncludeOperation)
            .OrderBy(op => TransformToolName(op), StringComparer.Ordinal)
            .ToList();

        activity?.SetTag("mcp.policy.candidates", included.Count);

        if (included.Count > _options.MaxToolCount)
        {
            _logger.LogWarning(
                "Tool count {Count} exceeds MaxToolCount {Max} — truncating to first {Max} (alphabetically)",
                included.Count, _options.MaxToolCount, _options.MaxToolCount);
            included = included.Take(_options.MaxToolCount).ToList();
        }

        activity?.SetTag("mcp.policy.published", included.Count);
        return included;
    }

    private static bool MatchesAnyPattern(string value, Regex[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.IsMatch(value))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Converts a simple glob pattern (*, ?) to a regex.
    /// </summary>
    private static Regex GlobToRegex(string glob)
    {
        var escaped = Regex.Escape(glob)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");
        return new Regex($"^{escaped}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private static string GetInnerTypeName(CanonicalType type)
    {
        var current = type;
        while (current.OfType is not null)
        {
            current = current.OfType;
        }
        return current.Name;
    }

    private static string CreateStableHashSuffix(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    private static int CalculateArgumentComplexity(IReadOnlyList<CanonicalArgument> arguments) =>
        arguments.Sum(argument => 1 + CalculateTypeComplexity(argument.Type));

    private static int CalculateTypeComplexity(CanonicalType type)
    {
        var baseCost = type.Kind switch
        {
            TypeKind.Scalar => 1,
            TypeKind.Enum => 1,
            TypeKind.InputObject => 4 + (type.Fields?.Sum(field => 1 + CalculateTypeComplexity(field.Type)) ?? 0),
            TypeKind.Object => 3 + (type.Fields?.Sum(field => 1 + CalculateTypeComplexity(field.Type)) ?? 0),
            TypeKind.Interface => 3 + (type.Fields?.Sum(field => 1 + CalculateTypeComplexity(field.Type)) ?? 0),
            TypeKind.Union => 3 + (type.PossibleTypes?.Sum(CalculateTypeComplexity) ?? 0),
            TypeKind.List => 2,
            TypeKind.NonNull => 1,
            _ => 1
        };

        return type.OfType is null ? baseCost : baseCost + CalculateTypeComplexity(type.OfType);
    }

    private static string InferDomain(CanonicalOperation operation)
    {
        var tokens = SplitIdentifier(operation.GraphQLFieldName)
            .Where(token => !ActionPrefixes.Contains(token))
            .Where(token => !StopWords.Contains(token))
            .Where(token => !StructuralSuffixes.Contains(token))
            .Where(token => !GenericDomainTokens.Contains(token))
            .ToList();

        if (tokens.Count == 0)
        {
            return "general";
        }

        return Singularize(tokens[^1]);
    }

    private static List<string> SplitIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var normalized = IdentifierBoundaryRegex().Replace(value, "$1_$2");
        return normalized
            .Split(['_', '-', '.', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .ToList();
    }

    private static string Singularize(string token)
    {
        if (token.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && token.Length > 3)
        {
            return token[..^3] + "y";
        }

        if (token.EndsWith("ses", StringComparison.OrdinalIgnoreCase) && token.Length > 3)
        {
            return token[..^2];
        }

        if (token.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
            !token.EndsWith("ss", StringComparison.OrdinalIgnoreCase) &&
            token.Length > 1)
        {
            return token[..^1];
        }

        return token;
    }

    private static readonly HashSet<string> ActionPrefixes =
    [
        "get", "list", "fetch", "find", "search", "create", "update", "delete", "remove", "add",
        "set", "count", "read", "lookup", "show", "load", "modify", "patch", "replace", "archive",
        "upsert", "view", "new", "return"
    ];

    private static readonly HashSet<string> StopWords =
    [
        "by", "for", "from", "with", "within", "in", "of", "on", "at", "via", "using"
    ];

    private static readonly HashSet<string> StructuralSuffixes =
    [
        "connection", "connections", "edge", "edges", "node", "nodes", "payload", "payloads",
        "response", "responses", "result", "results", "list", "lists", "page", "pages", "data",
        "item", "items", "collection", "collections", "viewer", "viewers"
    ];

    private static readonly HashSet<string> GenericDomainTokens =
    [
        "api", "graphql", "service", "services", "endpoint", "endpoints", "core"
    ];

    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex SanitizeRegex();

    [GeneratedRegex(@"_{2,}")]
    private static partial Regex CollapseUnderscoresRegex();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex IdentifierBoundaryRegex();
}
