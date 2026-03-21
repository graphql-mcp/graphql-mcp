package dev.graphqlmcp.autoconfigure;

import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector;
import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper;
import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper.GraphQLMCPConfig;
import dev.graphqlmcp.properties.GraphQLMCPProperties;
import dev.graphqlmcp.server.GraphQLMCPServer;
import graphql.schema.GraphQLSchema;
import java.util.Set;
import org.springframework.boot.autoconfigure.condition.ConditionalOnClass;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * Spring Boot auto-configuration for graphql-mcp. Activates when Spring GraphQL is on the classpath
 * and graphql.mcp.enabled=true.
 */
@Configuration
@ConditionalOnClass(GraphQLSchema.class)
@ConditionalOnProperty(
    prefix = "graphql.mcp",
    name = "enabled",
    havingValue = "true",
    matchIfMissing = true)
@EnableConfigurationProperties(GraphQLMCPProperties.class)
public class GraphQLMCPAutoConfiguration {

  @Bean
  public GraphQLSchemaIntrospector graphQLSchemaIntrospector() {
    return new GraphQLSchemaIntrospector();
  }

  @Bean
  public GraphQLToMCPToolMapper graphQLToMCPToolMapper(GraphQLMCPProperties properties) {
    var config =
        new GraphQLMCPConfig(
            properties.getToolPrefix(),
            mapNamingPolicy(properties.getNamingPolicy()),
            properties.isAllowMutations(),
            Set.copyOf(properties.getExcludedFields()),
            properties.getMaxOutputDepth(),
            properties.getMaxToolCount());
    return new GraphQLToMCPToolMapper(config);
  }

  @Bean
  public GraphQLMCPServer graphQLMCPServer(
      GraphQLSchema schema, GraphQLSchemaIntrospector introspector, GraphQLToMCPToolMapper mapper) {

    var operations = introspector.introspect(schema);
    var tools = mapper.map(operations);
    return new GraphQLMCPServer(tools);
  }

  private GraphQLToMCPToolMapper.NamingPolicy mapNamingPolicy(String policy) {
    if ("raw".equalsIgnoreCase(policy)) {
      return GraphQLToMCPToolMapper.NamingPolicy.RAW;
    }
    return GraphQLToMCPToolMapper.NamingPolicy.VERB_NOUN;
  }
}
