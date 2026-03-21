using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using GraphQL.MCP.Core.Observability;
using Microsoft.Extensions.Logging;

namespace GraphQL.MCP.Core.Canonical;

/// <summary>
/// Transforms raw GraphQL schema information from an <see cref="IGraphQLSchemaSource"/>
/// into a canonical model suitable for policy evaluation and tool publishing.
/// </summary>
public sealed class SchemaCanonicalizer
{
    private readonly IGraphQLSchemaSource _schemaSource;
    private readonly ILogger<SchemaCanonicalizer> _logger;

    public SchemaCanonicalizer(
        IGraphQLSchemaSource schemaSource,
        ILogger<SchemaCanonicalizer> logger)
    {
        _schemaSource = schemaSource;
        _logger = logger;
    }

    /// <summary>
    /// Introspects the GraphQL schema and returns all candidate operations.
    /// </summary>
    public async Task<CanonicalizationResult> CanonicalizeAsync(CancellationToken cancellationToken = default)
    {
        using var activity = McpActivitySource.Source.StartActivity("mcp.canonicalize");
        _logger.LogInformation("Starting schema canonicalization");

        var operations = await _schemaSource.GetOperationsAsync(cancellationToken);
        var types = await _schemaSource.GetTypesAsync(cancellationToken);

        var queries = new List<CanonicalOperation>();
        var mutations = new List<CanonicalOperation>();

        foreach (var op in operations)
        {
            // Skip introspection fields
            if (op.GraphQLFieldName.StartsWith("__", StringComparison.Ordinal))
            {
                _logger.LogDebug("Skipping introspection field: {FieldName}", op.GraphQLFieldName);
                continue;
            }

            switch (op.OperationType)
            {
                case OperationType.Query:
                    queries.Add(op);
                    break;
                case OperationType.Mutation:
                    mutations.Add(op);
                    break;
            }
        }

        _logger.LogInformation(
            "Canonicalization complete: {QueryCount} queries, {MutationCount} mutations discovered",
            queries.Count, mutations.Count);

        activity?.SetTag("mcp.canonicalize.queries", queries.Count);
        activity?.SetTag("mcp.canonicalize.mutations", mutations.Count);
        activity?.SetTag("mcp.canonicalize.types", types.Count);

        return new CanonicalizationResult
        {
            Queries = queries,
            Mutations = mutations,
            Types = types
        };
    }
}

/// <summary>
/// Result of schema canonicalization.
/// </summary>
public sealed class CanonicalizationResult
{
    public IReadOnlyList<CanonicalOperation> Queries { get; init; } = [];
    public IReadOnlyList<CanonicalOperation> Mutations { get; init; } = [];
    public IReadOnlyDictionary<string, CanonicalType> Types { get; init; } =
        new Dictionary<string, CanonicalType>();
}
