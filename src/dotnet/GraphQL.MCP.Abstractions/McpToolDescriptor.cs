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
    /// Explicit domain grouping used to cluster related tools in discovery UIs.
    /// </summary>
    public required string Domain { get; init; }

    /// <summary>
    /// Optional high-level category inferred from the GraphQL operation shape.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional discovery tags that help clients group related tools.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Optional semantic hints that help clients rank and present the tool.
    /// </summary>
    public McpSemanticHints SemanticHints { get; init; } = new();

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

/// <summary>
/// Additive semantic metadata for discovery UIs and ranking.
/// </summary>
public sealed class McpSemanticHints
{
    /// <summary>
    /// Short human-readable description of the tool's likely intent.
    /// </summary>
    public string Intent { get; init; } = "";

    /// <summary>
    /// Normalized keywords that describe the tool and its subject.
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
}
