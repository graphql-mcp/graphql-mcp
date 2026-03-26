package dev.graphqlmcp.publishing;

import static org.junit.jupiter.api.Assertions.*;

import dev.graphqlmcp.TestSchemas;
import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector;
import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper;
import graphql.schema.GraphQLSchema;
import java.util.List;
import java.util.Map;
import java.util.Set;
import org.junit.jupiter.api.Test;

class ToolPublisherTest {

  @Test
  void publishes_json_schema_and_graphql_query_for_nested_object_fields() {
    GraphQLSchema schema = TestSchemas.createSchema();
    GraphQLSchemaIntrospector introspector = new GraphQLSchemaIntrospector();
    GraphQLToMCPToolMapper mapper =
        new GraphQLToMCPToolMapper(
            new GraphQLToMCPToolMapper.GraphQLMCPConfig(
                "api",
                GraphQLToMCPToolMapper.NamingPolicy.VERB_NOUN,
                false,
                Set.of("secretNote"),
                3,
                10,
                false,
                25));
    ToolPublisher publisher = new ToolPublisher(mapper, mapperConfig());

    List<ToolDescriptor> tools = publisher.publish(introspector.introspect(schema), schema);

    assertEquals(2, tools.size());

    ToolDescriptor book =
        tools.stream()
            .filter(tool -> tool.graphQLFieldName().equals("book"))
            .findFirst()
            .orElseThrow();
    assertEquals("api_get_book", book.name());
    assertEquals("Find a book", book.description());
    assertEquals("object", book.inputSchema().get("type"));
    Map<?, ?> properties = (Map<?, ?>) book.inputSchema().get("properties");
    Map<?, ?> idProperty = (Map<?, ?>) properties.get("id");
    assertEquals("string", idProperty.get("type"));
    assertEquals("Book identifier", idProperty.get("description"));
    assertTrue(book.graphQLQuery().startsWith("query($id: ID!) { book(id: $id)"));
    assertTrue(book.graphQLQuery().contains("author { name email }"));
    assertFalse(book.graphQLQuery().contains("secretNote"));
    assertEquals("id", book.argumentMapping().get("id"));
    assertEquals("Query", book.category());
    assertTrue(book.tags().contains("query"));
  }

  @Test
  void publishes_mutations_with_mutation_prefix_when_allowed() {
    GraphQLSchema schema = TestSchemas.createSchema();
    GraphQLSchemaIntrospector introspector = new GraphQLSchemaIntrospector();
    GraphQLToMCPToolMapper mapper =
        new GraphQLToMCPToolMapper(
            new GraphQLToMCPToolMapper.GraphQLMCPConfig(
                null, GraphQLToMCPToolMapper.NamingPolicy.RAW, true, Set.of(), 3, 10, false, 25));
    ToolPublisher publisher = new ToolPublisher(mapper, mapperConfig());

    List<ToolDescriptor> tools = publisher.publish(introspector.introspect(schema), schema);

    ToolDescriptor mutation =
        tools.stream()
            .filter(tool -> tool.graphQLFieldName().equals("createBook"))
            .findFirst()
            .orElseThrow();

    assertEquals("[MUTATION] Create a book", mutation.description());
    assertTrue(
        mutation
            .graphQLQuery()
            .startsWith("mutation($title: String!) { createBook(title: $title)"));
  }

  private static GraphQLToMCPToolMapper.GraphQLMCPConfig mapperConfig() {
    return new GraphQLToMCPToolMapper.GraphQLMCPConfig(
        "api",
        GraphQLToMCPToolMapper.NamingPolicy.VERB_NOUN,
        false,
        Set.of("secretNote"),
        3,
        10,
        false,
        25);
  }
}
