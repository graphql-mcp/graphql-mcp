package dev.graphqlmcp.autoconfigure;

import static org.junit.jupiter.api.Assertions.*;

import dev.graphqlmcp.TestSchemas;
import dev.graphqlmcp.properties.GraphQLMCPProperties;
import dev.graphqlmcp.publishing.ToolDescriptor;
import dev.graphqlmcp.server.GraphQLMCPServer;
import graphql.schema.GraphQLSchema;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.springframework.boot.autoconfigure.AutoConfigurations;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;

class GraphQLMCPAutoConfigurationTest {

  private final ApplicationContextRunner contextRunner =
      new ApplicationContextRunner()
          .withConfiguration(AutoConfigurations.of(GraphQLMCPAutoConfiguration.class))
          .withBean(GraphQLSchema.class, TestSchemas::createSchema)
          .withPropertyValues(
              "graphql.mcp.tool-prefix=api",
              "graphql.mcp.naming-policy=verb-noun",
              "graphql.mcp.allow-mutations=false",
              "graphql.mcp.excluded-fields[0]=secretNote",
              "graphql.mcp.included-domains[0]=book",
              "graphql.mcp.require-descriptions=true",
              "graphql.mcp.min-description-length=5",
              "graphql.mcp.max-output-depth=3",
              "graphql.mcp.max-tool-count=10",
              "graphql.mcp.max-argument-count=5",
              "graphql.mcp.max-argument-complexity=25");

  @Test
  void binds_properties_and_publishes_tools_from_schema() {
    contextRunner.run(
        context -> {
          assertTrue(context.getSourceApplicationContext().containsBean("graphQLMCPServer"));
          assertTrue(context.getSourceApplicationContext().containsBean("toolExecutor"));
          assertTrue(context.getSourceApplicationContext().containsBean("graphQLExecutor"));
          assertTrue(context.getSourceApplicationContext().containsBean("mcpToolDescriptors"));

          GraphQLMCPProperties properties = context.getBean(GraphQLMCPProperties.class);
          assertEquals("api", properties.getToolPrefix());
          assertFalse(properties.isAllowMutations());
          assertEquals(1, properties.getExcludedFields().size());
          assertEquals(1, properties.getIncludedDomains().size());
          assertTrue(properties.isRequireDescriptions());
          assertEquals(5, properties.getMinDescriptionLength());
          assertEquals(5, properties.getMaxArgumentCount());
          assertEquals(25, properties.getMaxArgumentComplexity());

          GraphQLMCPServer server = context.getBean(GraphQLMCPServer.class);
          assertEquals(1, server.listTools().size());

          List<ToolDescriptor> tools = context.getBean("mcpToolDescriptors", List.class);
          assertEquals("api_get_book", tools.get(0).name());
        });
  }
}
