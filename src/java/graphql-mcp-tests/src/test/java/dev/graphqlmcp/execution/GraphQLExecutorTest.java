package dev.graphqlmcp.execution;

import static org.junit.jupiter.api.Assertions.*;

import dev.graphqlmcp.TestSchemas;
import graphql.schema.GraphQLSchema;
import java.util.Map;
import org.junit.jupiter.api.Test;

class GraphQLExecutorTest {

  @Test
  void executes_queries_with_variables_and_forwarded_headers() {
    GraphQLSchema schema = TestSchemas.createExecutionSchema();
    GraphQLExecutor executor = new GraphQLExecutor(schema);

    GraphQLExecutor.GraphQLExecutionResult result =
        executor.execute(
            "query($name: String!) { hello(name: $name) }",
            Map.of("name", "Ada"),
            Map.of("Authorization", "Bearer 123"));

    assertTrue(result.isSuccess());
    assertNull(result.errors());
    @SuppressWarnings("unchecked")
    Map<String, Object> data = (Map<String, Object>) result.data();
    assertEquals("Hello, Ada | auth=Bearer 123", data.get("hello"));
  }
}
