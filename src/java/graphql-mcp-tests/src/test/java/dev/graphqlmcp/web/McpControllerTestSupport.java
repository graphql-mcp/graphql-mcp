package dev.graphqlmcp.web;

import dev.graphqlmcp.TestSchemas;
import dev.graphqlmcp.execution.GraphQLExecutor;
import dev.graphqlmcp.execution.ToolExecutor;
import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector.OperationType;
import dev.graphqlmcp.publishing.ToolDescriptor;
import dev.graphqlmcp.server.GraphQLMCPServer;
import java.util.List;
import java.util.Map;

final class McpControllerTestSupport {

  private McpControllerTestSupport() {}

  static McpController createController() {
    return createController(GraphQLMCPServer.AuthorizationMetadata.none(), "streamable-http");
  }

  static McpController createController(GraphQLMCPServer.AuthorizationMetadata authMetadata) {
    return createController(authMetadata, "streamable-http");
  }

  static McpController createController(
      GraphQLMCPServer.AuthorizationMetadata authMetadata, String transportMode) {
    ToolDescriptor bookTool =
        new ToolDescriptor(
            "api_get_book",
            "Find a book",
            "Query",
            List.of("book", "query"),
            Map.of("type", "object"),
            "query($id: ID!) { book(id: $id) { title } }",
            "book",
            OperationType.QUERY,
            Map.of("id", "id"),
            "book",
            new ToolDescriptor.SemanticHints("retrieve", List.of("book", "query", "id")));
    ToolDescriptor tool =
        new ToolDescriptor(
            "api_get_hello",
            "Return a greeting",
            "Query",
            List.of("hello", "query"),
            Map.of("type", "object"),
            "query($name: String!) { hello(name: $name) }",
            "hello",
            OperationType.QUERY,
            Map.of("clientName", "name"),
            "hello",
            new ToolDescriptor.SemanticHints("retrieve", List.of("hello", "query", "name")));

    GraphQLExecutor executor = new GraphQLExecutor(TestSchemas.createExecutionSchema());
    ToolExecutor toolExecutor = new ToolExecutor(executor, List.of(tool, bookTool));
    GraphQLMCPServer server = new GraphQLMCPServer(List.of(tool, bookTool), authMetadata);
    return new McpController(server, toolExecutor, List.of(tool, bookTool), transportMode);
  }

  static GraphQLMCPServer.AuthorizationMetadata createAuthorizationMetadata() {
    return new GraphQLMCPServer.AuthorizationMetadata(
        "passthrough",
        List.of("orders.read", "orders.write"),
        new GraphQLMCPServer.OAuthMetadata(
            "https://auth.example.com",
            "https://auth.example.com/authorize",
            "https://auth.example.com/token",
            "https://auth.example.com/register",
            "https://auth.example.com/jwks",
            "https://docs.example.com/auth",
            List.of("code"),
            List.of("authorization_code", "refresh_token"),
            List.of("none")));
  }
}
