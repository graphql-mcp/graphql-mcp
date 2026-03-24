package dev.graphqlmcp.introspection;

import static org.junit.jupiter.api.Assertions.*;

import dev.graphqlmcp.TestSchemas;
import graphql.schema.GraphQLSchema;
import java.util.List;
import org.junit.jupiter.api.Test;

class GraphQLSchemaIntrospectorTest {

  @Test
  void introspects_root_queries_and_mutations_with_arguments() {
    GraphQLSchema schema = TestSchemas.createSchema();
    GraphQLSchemaIntrospector introspector = new GraphQLSchemaIntrospector();

    List<GraphQLSchemaIntrospector.CanonicalOperation> operations = introspector.introspect(schema);

    assertEquals(3, operations.size());
    assertAll(
        () -> assertEquals("hello", operations.get(0).name()),
        () -> assertEquals("Return a greeting", operations.get(0).description()),
        () ->
            assertEquals(
                GraphQLSchemaIntrospector.OperationType.QUERY, operations.get(0).operationType()),
        () -> assertEquals(1, operations.get(0).arguments().size()),
        () -> assertEquals("name", operations.get(0).arguments().get(0).name()),
        () -> assertTrue(operations.get(0).arguments().get(0).required()),
        () -> assertEquals("book", operations.get(1).name()),
        () ->
            assertEquals(
                GraphQLSchemaIntrospector.OperationType.QUERY, operations.get(1).operationType()),
        () -> assertEquals("createBook", operations.get(2).name()),
        () ->
            assertEquals(
                GraphQLSchemaIntrospector.OperationType.MUTATION,
                operations.get(2).operationType()),
        () -> assertEquals("title", operations.get(2).arguments().get(0).name()),
        () -> assertTrue(operations.get(2).arguments().get(0).required()));
  }
}
