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
    GraphQLMCPServer server = new GraphQLMCPServer(List.of(tool));

    GraphQLMCPServer.InitializeResult init = server.initialize();

    assertEquals("2025-06-18", init.protocolVersion());
    assertEquals("graphql-mcp", init.serverInfo().name());
    assertEquals("0.1.0", init.serverInfo().version());
    assertTrue(init.capabilities().tools().listChanged());
    assertTrue(init.capabilities().catalog().list());
    assertTrue(init.capabilities().catalog().search());
    assertEquals("domain", init.capabilities().catalog().grouping());
    assertEquals(1, server.listTools().size());
    assertTrue(server.hasTool("api_get_hello"));
    assertFalse(server.hasTool("missing"));
  }
}
