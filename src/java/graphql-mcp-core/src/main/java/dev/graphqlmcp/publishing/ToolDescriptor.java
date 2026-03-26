package dev.graphqlmcp.publishing;

import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector.OperationType;
import java.util.List;
import java.util.Map;

/** A fully-resolved MCP tool descriptor ready for serving over the MCP protocol. */
public record ToolDescriptor(
    String name,
    String description,
    String category,
    List<String> tags,
    Map<String, Object> inputSchema,
    String graphQLQuery,
    String graphQLFieldName,
    OperationType operationType,
    Map<String, String> argumentMapping,
    String domainGroup,
    SemanticHints semanticHints) {

  public ToolDescriptor(
      String name,
      String description,
      String category,
      List<String> tags,
      Map<String, Object> inputSchema,
      String graphQLQuery,
      String graphQLFieldName,
      OperationType operationType,
      Map<String, String> argumentMapping,
      String domainGroup) {
    this(
        name,
        description,
        category,
        tags,
        inputSchema,
        graphQLQuery,
        graphQLFieldName,
        operationType,
        argumentMapping,
        domainGroup,
        null);
  }

  public record SemanticHints(String intent, List<String> keywords) {
    public SemanticHints {
      keywords = keywords == null ? List.of() : List.copyOf(keywords);
    }
  }
}
