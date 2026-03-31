package dev.graphqlmcp.dgs.autoconfigure;

import static org.junit.jupiter.api.Assertions.assertTrue;

import dev.graphqlmcp.TestSchemas;
import graphql.schema.GraphQLSchema;
import org.junit.jupiter.api.Test;
import org.springframework.boot.autoconfigure.AutoConfigurations;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;

class DgsMCPAutoConfigurationTest {

  private final ApplicationContextRunner contextRunner =
      new ApplicationContextRunner()
          .withConfiguration(AutoConfigurations.of(DgsMCPAutoConfiguration.class))
          .withBean(GraphQLSchema.class, TestSchemas::createSchema);

  @Test
  void imports_graphql_mcp_support_for_dgs_apps() {
    contextRunner.run(
        context -> {
          assertTrue(context.containsBean("graphQLMCPServer"));
          assertTrue(context.containsBean("toolExecutor"));
          assertTrue(context.containsBean("mcpController"));
        });
  }
}
