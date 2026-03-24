package dev.graphqlmcp.execution;

import com.fasterxml.jackson.databind.ObjectMapper;
import graphql.ExecutionInput;
import graphql.ExecutionResult;
import graphql.GraphQL;
import graphql.schema.GraphQLSchema;
import java.util.List;
import java.util.Map;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/** Executes GraphQL queries against a graphql-java schema. */
public class GraphQLExecutor {

  private static final Logger log = LoggerFactory.getLogger(GraphQLExecutor.class);
  private static final ObjectMapper MAPPER = new ObjectMapper();

  private final GraphQL graphQL;

  public GraphQLExecutor(GraphQLSchema schema) {
    this.graphQL = GraphQL.newGraphQL(schema).build();
  }

  /** Executes a GraphQL query and returns a portable result. */
  public GraphQLExecutionResult execute(
      String query, Map<String, Object> variables, Map<String, String> headers) {
    log.debug("Executing GraphQL query: {}", query);

    var inputBuilder = ExecutionInput.newExecutionInput().query(query);

    if (variables != null && !variables.isEmpty()) {
      inputBuilder.variables(variables);
    }

    // Forward headers via graphQLContext so resolvers can access them
    if (headers != null && !headers.isEmpty()) {
      inputBuilder.graphQLContext(builder -> headers.forEach(builder::of));
    }

    ExecutionResult result = graphQL.execute(inputBuilder.build());

    List<GraphQLExecutionError> errors = null;
    if (result.getErrors() != null && !result.getErrors().isEmpty()) {
      errors =
          result.getErrors().stream()
              .map(e -> new GraphQLExecutionError(e.getMessage(), e.getPath()))
              .toList();
    }

    return new GraphQLExecutionResult(result.getData(), errors);
  }

  public record GraphQLExecutionResult(Object data, List<GraphQLExecutionError> errors) {
    public boolean isSuccess() {
      return errors == null || errors.isEmpty();
    }
  }

  public record GraphQLExecutionError(String message, List<Object> path) {}
}
