package dev.graphqlmcp.server;

import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper.MCPToolDescriptor;

import java.util.List;

/**
 * Core MCP server that manages tool registration and invocation.
 * Framework adapters (Spring, etc.) delegate to this class.
 */
public class GraphQLMCPServer {

    private final List<MCPToolDescriptor> tools;

    public GraphQLMCPServer(List<MCPToolDescriptor> tools) {
        this.tools = List.copyOf(tools);
    }

    /**
     * Returns the MCP initialize response capabilities.
     */
    public InitializeResult initialize() {
        return new InitializeResult(
                "2025-06-18",
                new Capabilities(new ToolCapabilities(true)),
                new ServerInfo("graphql-mcp", "0.1.0")
        );
    }

    /**
     * Returns all registered tools.
     */
    public List<MCPToolDescriptor> listTools() {
        return tools;
    }

    /**
     * Checks if a tool exists.
     */
    public boolean hasTool(String name) {
        return tools.stream().anyMatch(t -> t.name().equals(name));
    }

    // --- Response models ---

    public record InitializeResult(
            String protocolVersion,
            Capabilities capabilities,
            ServerInfo serverInfo
    ) {}

    public record Capabilities(ToolCapabilities tools) {}
    public record ToolCapabilities(boolean listChanged) {}
    public record ServerInfo(String name, String version) {}
}
