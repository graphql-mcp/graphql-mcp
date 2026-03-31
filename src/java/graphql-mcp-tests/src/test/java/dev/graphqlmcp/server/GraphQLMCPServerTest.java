package dev.graphqlmcp.server;

import static org.junit.jupiter.api.Assertions.*;

import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector.OperationType;
import dev.graphqlmcp.publishing.ToolDescriptor;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class GraphQLMCPServerTest {

  @Test
  void initializes_and_exposes_registered_tools() {
    ToolDescriptor tool =
        new ToolDescriptor(
            "api_get_hello",
            "Return a greeting",
            "Query",
            List.of("query"),
            Map.of("type", "object"),
            "query { hello }",
            "hello",
            OperationType.QUERY,
            Map.of(),
            "hello");
    GraphQLMCPServer server =
        new GraphQLMCPServer(
            List.of(tool),
            new GraphQLMCPServer.AuthorizationMetadata(
                "passthrough",
                List.of("greeting.read"),
                new GraphQLMCPServer.OAuthMetadata(
                    "https://auth.example.com",
                    "https://auth.example.com/authorize",
                    "https://auth.example.com/token",
                    null,
                    null,
                    null,
                    List.of("code"),
                    List.of("authorization_code"),
                    List.of("none"))));

    GraphQLMCPServer.InitializeResult init = server.initialize();

    assertEquals("2025-06-18", init.protocolVersion());
    assertEquals("graphql-mcp", init.serverInfo().name());
    assertEquals("0.1.0", init.serverInfo().version());
    assertTrue(init.capabilities().tools().listChanged());
    assertTrue(init.capabilities().prompts().listChanged());
    assertTrue(init.capabilities().resources().listChanged());
    assertTrue(init.capabilities().resources().read());
    assertTrue(init.capabilities().catalog().list());
    assertTrue(init.capabilities().catalog().search());
    assertEquals("domain", init.capabilities().catalog().grouping());
    assertEquals("passthrough", init.capabilities().authorization().mode());
    assertEquals(
        GraphQLMCPServer.AuthorizationMetadata.RESOURCE_URI,
        init.capabilities().authorization().oauth2().resource());
    assertTrue(init.capabilities().authorization().oauth2().metadata());
    assertEquals(1, server.listTools().size());
    assertTrue(server.hasTool("api_get_hello"));
    assertFalse(server.hasTool("missing"));
  }
}
