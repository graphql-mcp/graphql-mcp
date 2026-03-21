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
            baseName = baseName[..60] + "_" + Math.Abs(baseName.GetHashCode()).ToString("x3")[..3];
        }

        return baseName;
    }

    /// <inheritdoc />
    public int GetMaxDepth() => _options.MaxOutputDepth;

    /// <inheritdoc />
    public int GetMaxToolCount() => _options.MaxToolCount;

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

    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex SanitizeRegex();

    [GeneratedRegex(@"_{2,}")]
    private static partial Regex CollapseUnderscoresRegex();
}
