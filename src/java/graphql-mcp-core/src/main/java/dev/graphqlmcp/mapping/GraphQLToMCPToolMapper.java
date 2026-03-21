package dev.graphqlmcp.mapping;

import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector.CanonicalOperation;
import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector.OperationType;
import java.util.*;
import java.util.stream.Collectors;

/**
 * Transforms canonical GraphQL operations into MCP tool descriptors. Java equivalent of
 * ToolPublisher + PolicyEngine in .NET.
 */
public class GraphQLToMCPToolMapper {

  private final GraphQLMCPConfig config;

  public GraphQLToMCPToolMapper(GraphQLMCPConfig config) {
    this.config = config;
  }

  /** Filters and maps operations to MCP tool descriptors. */
  public List<MCPToolDescriptor> map(List<CanonicalOperation> operations) {
    return operations.stream()
        .filter(this::shouldInclude)
        .sorted(Comparator.comparing(op -> transformName(op)))
        .limit(config.maxToolCount())
        .map(this::toDescriptor)
        .collect(Collectors.toUnmodifiableList());
  }

  private boolean shouldInclude(CanonicalOperation op) {
    if (op.graphQLFieldName().startsWith("__")) return false;
    if (op.operationType() == OperationType.MUTATION && !config.allowMutations()) return false;
    if (config.excludedFields().contains(op.graphQLFieldName())) return false;
    return true;
  }

  private String transformName(CanonicalOperation op) {
    String base =
        switch (config.namingPolicy()) {
          case VERB_NOUN ->
              op.operationType() == OperationType.QUERY
                  ? "get_" + op.graphQLFieldName()
                  : op.graphQLFieldName();
          case RAW -> op.graphQLFieldName();
        };

    if (config.toolPrefix() != null && !config.toolPrefix().isEmpty()) {
      base = config.toolPrefix() + "_" + base;
    }

    return base.replaceAll("[^a-zA-Z0-9_]", "_").replaceAll("_{2,}", "_");
  }

  private MCPToolDescriptor toDescriptor(CanonicalOperation op) {
    String name = transformName(op);
    String prefix = op.operationType() == OperationType.MUTATION ? "[MUTATION] " : "";
    String description =
        prefix
            + (op.description() != null
                ? op.description()
                : "GraphQL "
                    + op.operationType().name().toLowerCase()
                    + ": "
                    + op.graphQLFieldName());

    return new MCPToolDescriptor(name, description, op.graphQLFieldName(), op.operationType());
  }

  public record MCPToolDescriptor(
      String name, String description, String graphQLFieldName, OperationType operationType) {}

  public record GraphQLMCPConfig(
      String toolPrefix,
      NamingPolicy namingPolicy,
      boolean allowMutations,
      Set<String> excludedFields,
      int maxOutputDepth,
      int maxToolCount) {
    public static GraphQLMCPConfig defaults() {
      return new GraphQLMCPConfig(null, NamingPolicy.VERB_NOUN, false, Set.of(), 3, 50);
    }
  }

  public enum NamingPolicy {
    VERB_NOUN,
    RAW
  }
}
