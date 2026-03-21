using GraphQL.MCP.Abstractions;

namespace GraphQL.MCP.AspNetCore;

/// <summary>
/// Thread-safe registry for MCP tool descriptors.
/// Populated at startup by <see cref="McpToolInitializationService"/>.
/// </summary>
public sealed class McpToolRegistry
{
    private volatile IReadOnlyList<McpToolDescriptor> _tools = [];

    /// <summary>
    /// Gets the current set of published MCP tools.
    /// </summary>
    public IReadOnlyList<McpToolDescriptor> Tools => _tools;

    /// <summary>
    /// Sets the tool list. Called once during startup initialization.
    /// </summary>
    internal void SetTools(IReadOnlyList<McpToolDescriptor> tools)
    {
        _tools = tools;
    }
}
