using GraphQL.MCP.Abstractions.Canonical;

namespace GraphQL.MCP.Abstractions.Policy;

/// <summary>
/// Evaluates whether operations should be published as MCP tools
/// and transforms tool metadata according to policy rules.
/// </summary>
public interface IMcpPolicy
{
    /// <summary>
    /// Determines whether an operation should be published as an MCP tool.
    /// </summary>
    bool ShouldIncludeOperation(CanonicalOperation operation);

    /// <summary>
    /// Transforms the GraphQL field name into an MCP tool name.
    /// </summary>
    string TransformToolName(CanonicalOperation operation);

    /// <summary>
    /// Returns the maximum selection set depth for auto-generated queries.
    /// </summary>
    int GetMaxDepth();

    /// <summary>
    /// Returns the maximum number of tools to publish.
    /// </summary>
    int GetMaxToolCount();

    /// <summary>
    /// Checks whether a field name should be excluded from selection sets.
    /// This applies ExcludedFields patterns to nested fields, not just root operations.
    /// </summary>
    bool IsFieldExcluded(string fieldName);

    /// <summary>
    /// Filters and transforms a list of operations according to policy.
    /// Returns only the operations that pass all policy checks, up to MaxToolCount.
    /// </summary>
    IReadOnlyList<CanonicalOperation> Apply(IEnumerable<CanonicalOperation> operations);
}
