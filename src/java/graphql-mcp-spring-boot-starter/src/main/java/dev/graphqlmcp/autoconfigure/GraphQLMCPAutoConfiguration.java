package dev.graphqlmcp.autoconfigure;

import dev.graphqlmcp.execution.GraphQLExecutor;
import dev.graphqlmcp.execution.ToolExecutor;
import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector;
import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper;
import dev.graphqlmcp.properties.GraphQLMCPProperties;
import dev.graphqlmcp.publishing.ToolDescriptor;
import dev.graphqlmcp.publishing.ToolPublisher;
import dev.graphqlmcp.server.GraphQLMCPServer;
import graphql.schema.GraphQLSchema;
import java.util.List;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.boot.autoconfigure.condition.ConditionalOnClass;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * Spring Boot auto-configuration for graphql-mcp. Activates when graphql-java is on the classpath
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

  private static final Logger log = LoggerFactory.getLogger(GraphQLMCPAutoConfiguration.class);

  @Bean
  public GraphQLSchemaIntrospector graphQLSchemaIntrospector() {
    return new GraphQLSchemaIntrospector();
  }

  @Bean
  public GraphQLToMCPToolMapper graphQLToMCPToolMapper(GraphQLMCPProperties properties) {
    var config = buildConfig(properties);
    return new GraphQLToMCPToolMapper(config);
  }

  @Bean
  public ToolPublisher toolPublisher(
      GraphQLToMCPToolMapper mapper, GraphQLMCPProperties properties) {
    return new ToolPublisher(mapper, buildConfig(properties));
  }

  @Bean
  public List<ToolDescriptor> mcpToolDescriptors(
      GraphQLSchema schema, GraphQLSchemaIntrospector introspector, ToolPublisher publisher) {
    var operations = introspector.introspect(schema);
    var tools = publisher.publish(operations, schema);
    log.info("Published {} MCP tools", tools.size());
    return tools;
  }

  @Bean
  public GraphQLMCPServer graphQLMCPServer(
      List<ToolDescriptor> tools, GraphQLMCPProperties properties) {
    var authorization = properties.getAuthorization();
    var metadata = authorization.getMetadata();
    var authMetadata =
        new GraphQLMCPServer.AuthorizationMetadata(
            authorization.getMode(),
            List.copyOf(authorization.getRequiredScopes()),
            new GraphQLMCPServer.OAuthMetadata(
                metadata.getIssuer(),
                metadata.getAuthorizationEndpoint(),
                metadata.getTokenEndpoint(),
                metadata.getRegistrationEndpoint(),
                metadata.getJwksUri(),
                metadata.getServiceDocumentation(),
                List.copyOf(metadata.getResponseTypesSupported()),
                List.copyOf(metadata.getGrantTypesSupported()),
                List.copyOf(metadata.getTokenEndpointAuthMethodsSupported())));
    return new GraphQLMCPServer(tools, authMetadata);
  }

  @Bean
  public GraphQLExecutor graphQLExecutor(GraphQLSchema schema) {
    return new GraphQLExecutor(schema);
  }

  @Bean
  public ToolExecutor toolExecutor(GraphQLExecutor executor, List<ToolDescriptor> tools) {
    return new ToolExecutor(executor, tools);
  }

  private GraphQLToMCPToolMapper.GraphQLMCPConfig buildConfig(GraphQLMCPProperties properties) {
    return GraphQLMCPPolicyProfiles.resolve(properties);
  }
}
