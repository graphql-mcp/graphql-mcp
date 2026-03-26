using System.Text.Json;
using GraphQL.MCP.Abstractions.Canonical;

namespace GraphQL.MCP.Abstractions;

/// <summary>
/// Describes an MCP tool generated from a GraphQL operation.
/// </summary>
public sealed class McpToolDescriptor
{
    /// <summary>
    /// The MCP tool name (after naming policy + prefix).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Human-readable description for the AI client.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional high-level category inferred from the GraphQL operation shape.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional discovery tags that help clients group related tools.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// JSON Schema describing the tool's input parameters.
    /// </summary>
    public required JsonDocument InputSchema { get; init; }

    /// <summary>
    /// The GraphQL query/mutation string to execute when this tool is called.
    /// </summary>
    public required string GraphQLQuery { get; init; }

    /// <summary>
    /// Whether this is a query or mutation.
    /// </summary>
    public OperationType OperationType { get; init; }

    /// <summary>
    /// The original GraphQL field name.
    /// </summary>
    public required string GraphQLFieldName { get; init; }

    /// <summary>
    /// Maps MCP tool argument names to GraphQL variable names.
    /// </summary>
    public IReadOnlyDictionary<string, string> ArgumentMapping { get; init; } = new Dictionary<string, string>();
}
