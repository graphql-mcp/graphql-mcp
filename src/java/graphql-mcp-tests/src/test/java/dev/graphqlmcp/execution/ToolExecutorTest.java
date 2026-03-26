package dev.graphqlmcp.execution;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import dev.graphqlmcp.TestSchemas;
import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector.OperationType;
import dev.graphqlmcp.publishing.ToolDescriptor;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class ToolExecutorTest {

  private static final ObjectMapper MAPPER = new ObjectMapper();

  @Test
  void executes_tool_calls_and_serializes_result_json() throws Exception {
    GraphQLExecutor executor = new GraphQLExecutor(TestSchemas.createExecutionSchema());
    ToolExecutor toolExecutor =
        new ToolExecutor(
            executor,
            List.of(
                new ToolDescriptor(
                    "api_get_hello",
                    "Return a greeting",
                    "Query",
                    List.of("query"),
                    Map.of("type", "object"),
                    "query($name: String!) { hello(name: $name) }",
                    "hello",
                    OperationType.QUERY,
                    Map.of("clientName", "name"),
                    "hello")));

    ToolExecutor.ToolResult result =
        toolExecutor.execute(
            "api_get_hello", Map.of("clientName", "Ada"), Map.of("Authorization", "Bearer 456"));

    assertTrue(result.isSuccess());
    assertNull(result.errorMessage());
    assertNotNull(result.content());
    assertEquals(
        "Hello, Ada | auth=Bearer 456", MAPPER.readTree(result.content()).get("hello").asText());
  }

  @Test
  void returns_error_for_unknown_tool() {
    ToolExecutor toolExecutor =
        new ToolExecutor(new GraphQLExecutor(TestSchemas.createExecutionSchema()), List.of());

    ToolExecutor.ToolResult result = toolExecutor.execute("missing", Map.of(), Map.of());

    assertFalse(result.isSuccess());
    assertEquals("Tool 'missing' not found.", result.errorMessage());
  }
}
