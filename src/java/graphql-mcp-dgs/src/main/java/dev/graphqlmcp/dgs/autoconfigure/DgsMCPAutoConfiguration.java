package dev.graphqlmcp.dgs.autoconfigure;

import dev.graphqlmcp.autoconfigure.GraphQLMCPAutoConfiguration;
import org.springframework.boot.autoconfigure.condition.ConditionalOnClass;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Import;

/**
 * Thin adapter for Netflix DGS applications. DGS now runs on top of Spring for GraphQL, so the
 * existing Spring GraphQL MCP integration can be reused when DGS is on the classpath.
 */
@Configuration
@ConditionalOnClass(
    name = {"com.netflix.graphql.dgs.DgsQueryExecutor", "graphql.schema.GraphQLSchema"})
@Import(GraphQLMCPAutoConfiguration.class)
public class DgsMCPAutoConfiguration {}
