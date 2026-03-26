package dev.graphqlmcp.server;

import dev.graphqlmcp.publishing.ToolDescriptor;
import java.util.List;

/**
 * Core MCP server that manages tool registration and protocol responses. Framework adapters
 * (Spring, etc.) delegate to this class.
 */
public class GraphQLMCPServer {

  private final List<ToolDescriptor> tools;

  public GraphQLMCPServer(List<ToolDescriptor> tools) {
    this.tools = List.copyOf(tools);
  }

  /** Returns the MCP initialize response capabilities. */
  public InitializeResult initialize() {
    return new InitializeResult(
        "2025-06-18",
        new Capabilities(new ToolCapabilities(true), new CatalogCapabilities(true, "domain")),
        new ServerInfo("graphql-mcp", "0.1.0"));
  }

  /** Returns all registered tools. */
  public List<ToolDescriptor> listTools() {
    return tools;
  }

  /** Checks if a tool exists. */
  public boolean hasTool(String name) {
    return tools.stream().anyMatch(t -> t.name().equals(name));
  }

  // --- Response models ---

  public record InitializeResult(
      String protocolVersion, Capabilities capabilities, ServerInfo serverInfo) {}

  public record Capabilities(ToolCapabilities tools, CatalogCapabilities catalog) {}

  public record ToolCapabilities(boolean listChanged) {}

  public record CatalogCapabilities(boolean list, String grouping) {}

  public record ServerInfo(String name, String version) {}
}
