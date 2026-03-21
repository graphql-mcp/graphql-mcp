package dev.graphqlmcp.annotation;

import dev.graphqlmcp.autoconfigure.GraphQLMCPAutoConfiguration;
import java.lang.annotation.*;
import org.springframework.context.annotation.Import;

/**
 * Enables graphql-mcp for a Spring Boot application. Apply to your main application class
 * alongside @SpringBootApplication.
 *
 * <pre>
 * &#064;EnableGraphQLMCP
 * &#064;SpringBootApplication
 * public class MyApp {
 *     public static void main(String[] args) {
 *         SpringApplication.run(MyApp.class, args);
 *     }
 * }
 * </pre>
 */
@Target(ElementType.TYPE)
@Retention(RetentionPolicy.RUNTIME)
@Documented
@Import(GraphQLMCPAutoConfiguration.class)
public @interface EnableGraphQLMCP {}
