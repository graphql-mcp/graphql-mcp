package dev.graphqlmcp.publishing;

import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector.CanonicalArgument;
import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector.CanonicalOperation;
import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector.OperationType;
import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper;
import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper.GraphQLMCPConfig;
import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper.MCPToolDescriptor;
import graphql.schema.*;
import java.util.*;

/**
 * Publishes MCP tool descriptors with JSON Schema inputs and auto-generated GraphQL queries. Builds
 * on top of GraphQLToMCPToolMapper for naming/filtering, then adds query generation and schema.
 */
public class ToolPublisher {

  private final GraphQLToMCPToolMapper mapper;
  private final GraphQLMCPConfig config;

  public ToolPublisher(GraphQLToMCPToolMapper mapper, GraphQLMCPConfig config) {
    this.mapper = mapper;
    this.config = config;
  }

  /** Publishes full tool descriptors from canonical operations and the GraphQL schema. */
  public List<ToolDescriptor> publish(List<CanonicalOperation> operations, GraphQLSchema schema) {
    List<MCPToolDescriptor> mcpTools = mapper.map(operations);

    // Build a lookup from graphQLFieldName to canonical operation
    Map<String, CanonicalOperation> opLookup = new HashMap<>();
    for (CanonicalOperation op : operations) {
      opLookup.put(op.graphQLFieldName(), op);
    }

    List<ToolDescriptor> result = new ArrayList<>();
    for (MCPToolDescriptor mcpTool : mcpTools) {
      CanonicalOperation op = opLookup.get(mcpTool.graphQLFieldName());
      if (op == null) continue;

      Map<String, Object> inputSchema = buildInputSchema(op);
      String graphQLQuery = buildGraphQLQuery(op, schema);
      Map<String, String> argumentMapping = buildArgumentMapping(op);

      result.add(
          new ToolDescriptor(
              mcpTool.name(),
              mcpTool.description(),
              mcpTool.category(),
              mcpTool.tags(),
              inputSchema,
              graphQLQuery,
              mcpTool.graphQLFieldName(),
              mcpTool.operationType(),
              argumentMapping));
    }
    return List.copyOf(result);
  }

  private Map<String, Object> buildInputSchema(CanonicalOperation op) {
    Map<String, Object> schema = new LinkedHashMap<>();
    schema.put("type", "object");

    Map<String, Object> properties = new LinkedHashMap<>();
    List<String> required = new ArrayList<>();

    for (CanonicalArgument arg : op.arguments()) {
      Map<String, Object> prop = new LinkedHashMap<>();
      prop.put("type", "string"); // simplified — all args as string for now
      if (arg.description() != null) {
        prop.put("description", arg.description());
      }
      if (arg.defaultValue() != null) {
        prop.put("default", arg.defaultValue().toString());
      }
      properties.put(arg.name(), prop);

      if (arg.required()) {
        required.add(arg.name());
      }
    }

    schema.put("properties", properties);
    if (!required.isEmpty()) {
      schema.put("required", required);
    }
    schema.put("additionalProperties", false);

    return schema;
  }

  private String buildGraphQLQuery(CanonicalOperation op, GraphQLSchema schema) {
    String opKeyword = op.operationType() == OperationType.QUERY ? "query" : "mutation";

    StringBuilder varDecl = new StringBuilder();
    StringBuilder fieldArgs = new StringBuilder();

    if (!op.arguments().isEmpty()) {
      List<String> varParts = new ArrayList<>();
      List<String> argParts = new ArrayList<>();

      GraphQLObjectType rootType =
          op.operationType() == OperationType.QUERY
              ? schema.getQueryType()
              : schema.getMutationType();

      GraphQLFieldDefinition fieldDef =
          rootType != null ? rootType.getFieldDefinition(op.graphQLFieldName()) : null;

      for (CanonicalArgument arg : op.arguments()) {
        String graphQLType = "String";
        if (fieldDef != null) {
          GraphQLArgument gqlArg = fieldDef.getArgument(arg.name());
          if (gqlArg != null) {
            graphQLType = typeToString(gqlArg.getType());
          }
        }
        varParts.add("$" + arg.name() + ": " + graphQLType);
        argParts.add(arg.name() + ": $" + arg.name());
      }

      varDecl.append("(").append(String.join(", ", varParts)).append(")");
      fieldArgs.append("(").append(String.join(", ", argParts)).append(")");
    }

    // Build selection set
    String selectionSet = "";
    GraphQLObjectType rootType =
        op.operationType() == OperationType.QUERY
            ? schema.getQueryType()
            : schema.getMutationType();
    if (rootType != null) {
      GraphQLFieldDefinition fieldDef = rootType.getFieldDefinition(op.graphQLFieldName());
      if (fieldDef != null) {
        selectionSet =
            buildSelectionSet(fieldDef.getType(), config.maxOutputDepth(), new HashSet<>());
      }
    }

    return opKeyword + varDecl + " { " + op.graphQLFieldName() + fieldArgs + selectionSet + " }";
  }

  private String buildSelectionSet(GraphQLType type, int depth, Set<String> visited) {
    GraphQLType unwrapped = GraphQLTypeUtil.unwrapAll(type);

    if (unwrapped instanceof GraphQLScalarType || unwrapped instanceof GraphQLEnumType) {
      return "";
    }

    if (depth <= 0) {
      return "";
    }

    if (unwrapped instanceof GraphQLObjectType objType) {
      if (visited.contains(objType.getName())) return "";
      Set<String> nextVisited = new HashSet<>(visited);
      nextVisited.add(objType.getName());

      List<String> fields = new ArrayList<>();
      for (GraphQLFieldDefinition field : objType.getFieldDefinitions()) {
        if (field.getName().startsWith("__")) continue;
        if (config.excludedFields().contains(field.getName())) continue;

        String inner = buildSelectionSet(field.getType(), depth - 1, nextVisited);
        fields.add(field.getName() + inner);
      }
      return fields.isEmpty() ? "" : " { " + String.join(" ", fields) + " }";
    }

    if (unwrapped instanceof GraphQLInterfaceType ifaceType) {
      if (visited.contains(ifaceType.getName())) return "";
      Set<String> nextVisited = new HashSet<>(visited);
      nextVisited.add(ifaceType.getName());

      List<String> fields = new ArrayList<>();
      fields.add("__typename");
      for (GraphQLFieldDefinition field : ifaceType.getFieldDefinitions()) {
        if (field.getName().startsWith("__")) continue;
        if (config.excludedFields().contains(field.getName())) continue;

        String inner = buildSelectionSet(field.getType(), depth - 1, nextVisited);
        fields.add(field.getName() + inner);
      }
      return fields.isEmpty() ? "" : " { " + String.join(" ", fields) + " }";
    }

    if (unwrapped instanceof GraphQLUnionType unionType) {
      if (visited.contains(unionType.getName())) return "";
      Set<String> nextVisited = new HashSet<>(visited);
      nextVisited.add(unionType.getName());

      List<String> fragments = new ArrayList<>();
      fragments.add("__typename");
      for (GraphQLNamedOutputType member : unionType.getTypes()) {
        String inner = buildSelectionSet(member, depth - 1, nextVisited);
        if (!inner.isEmpty()) {
          fragments.add("... on " + member.getName() + inner);
        }
      }
      return " { " + String.join(" ", fragments) + " }";
    }

    return "";
  }

  private String typeToString(GraphQLType type) {
    if (type instanceof GraphQLNonNull nonNull) {
      return typeToString(nonNull.getWrappedType()) + "!";
    }
    if (type instanceof GraphQLList list) {
      return "[" + typeToString(list.getWrappedType()) + "]";
    }
    if (type instanceof GraphQLNamedType named) {
      return named.getName();
    }
    return "String";
  }

  private Map<String, String> buildArgumentMapping(CanonicalOperation op) {
    Map<String, String> mapping = new LinkedHashMap<>();
    for (CanonicalArgument arg : op.arguments()) {
      mapping.put(arg.name(), arg.name());
    }
    return mapping;
  }
}
