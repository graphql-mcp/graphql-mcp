using GraphQL.MCP.Abstractions.Canonical;

namespace GraphQL.MCP.Abstractions;

/// <summary>
/// Extracts canonical operations and types from a GraphQL framework's schema.
/// Implemented by each framework adapter (Hot Chocolate, graphql-dotnet, etc.).
/// </summary>
public interface IGraphQLSchemaSource
{
    /// <summary>
    /// Discovers all root-level query and mutation fields from the schema.
    /// </summary>
    Task<IReadOnlyList<CanonicalOperation>> GetOperationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all named types in the schema for reference resolution.
    /// </summary>
    Task<IReadOnlyDictionary<string, CanonicalType>> GetTypesAsync(CancellationToken cancellationToken = default);
}
