package dev.graphqlmcp.mapping;

import static org.junit.jupiter.api.Assertions.*;

import dev.graphqlmcp.TestSchemas;
import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector;
import java.util.List;
import java.util.Set;
import org.junit.jupiter.api.Test;

class GraphQLToMCPToolMapperTest {

  @Test
  void filters_mutations_and_orders_tools_by_generated_name() {
    GraphQLSchemaIntrospector introspector = new GraphQLSchemaIntrospector();
    List<GraphQLSchemaIntrospector.CanonicalOperation> operations =
        introspector.introspect(TestSchemas.createSchema());

    GraphQLToMCPToolMapper mapper =
        new GraphQLToMCPToolMapper(
            new GraphQLToMCPToolMapper.GraphQLMCPConfig(
                "api", GraphQLToMCPToolMapper.NamingPolicy.VERB_NOUN, false, Set.of(), 3, 10));

    List<GraphQLToMCPToolMapper.MCPToolDescriptor> tools = mapper.map(operations);

    assertEquals(2, tools.size());
    assertAll(
        () -> assertEquals("api_get_book", tools.get(0).name()),
        () -> assertEquals("api_get_hello", tools.get(1).name()),
        () -> assertEquals("Find a book", tools.get(0).description()),
        () -> assertEquals("Return a greeting", tools.get(1).description()));
  }

  @Test
  void respects_exclusions_and_max_tool_count() {
    GraphQLSchemaIntrospector introspector = new GraphQLSchemaIntrospector();
    List<GraphQLSchemaIntrospector.CanonicalOperation> operations =
        introspector.introspect(TestSchemas.createSchema());

    GraphQLToMCPToolMapper mapper =
        new GraphQLToMCPToolMapper(
            new GraphQLToMCPToolMapper.GraphQLMCPConfig(
                null, GraphQLToMCPToolMapper.NamingPolicy.RAW, true, Set.of("book"), 3, 1));

    List<GraphQLToMCPToolMapper.MCPToolDescriptor> tools = mapper.map(operations);

    assertEquals(1, tools.size());
    assertEquals("createBook", tools.get(0).name());
  }
}
