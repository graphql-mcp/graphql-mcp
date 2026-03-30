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

  private static final String GENERAL_DOMAIN = "general";

  private static final Set<String> ACTION_PREFIXES =
      Set.of(
          "get", "list", "fetch", "find", "search", "create", "update", "delete", "remove", "add",
          "set", "count", "read", "lookup", "show", "load", "modify", "patch", "replace", "archive",
          "upsert", "view", "new", "return");

  private static final Set<String> STOP_WORDS =
      Set.of("by", "for", "from", "with", "within", "in", "of", "on", "at", "via", "using");

  private static final Set<String> STRUCTURAL_SUFFIXES =
      Set.of(
          "connection",
          "connections",
          "edge",
          "edges",
          "node",
          "nodes",
          "payload",
          "payloads",
          "response",
          "responses",
          "result",
          "results",
          "list",
          "lists",
          "page",
          "pages",
          "data",
          "item",
          "items",
          "collection",
          "collections",
          "viewer",
          "viewers");

  private static final Set<String> GENERIC_DOMAIN_TOKENS =
      Set.of("api", "graphql", "service", "services", "endpoint", "endpoints", "core");

  private final GraphQLToMCPToolMapper mapper;
  private final GraphQLMCPConfig config;

  public ToolPublisher(GraphQLToMCPToolMapper mapper, GraphQLMCPConfig config) {
    this.mapper = mapper;
    this.config = config;
  }

  /** Publishes full tool descriptors from canonical operations and the GraphQL schema. */
  public List<ToolDescriptor> publish(List<CanonicalOperation> operations, GraphQLSchema schema) {
    List<MCPToolDescriptor> mcpTools = mapper.map(operations);

    Map<String, CanonicalOperation> opLookup = new HashMap<>();
    for (CanonicalOperation op : operations) {
      opLookup.put(op.graphQLFieldName(), op);
    }

    List<ToolDescriptor> result = new ArrayList<>();
    for (MCPToolDescriptor mcpTool : mcpTools) {
      CanonicalOperation op = opLookup.get(mcpTool.graphQLFieldName());
      if (op == null) {
        continue;
      }

      Map<String, Object> inputSchema = buildInputSchema(op);
      String graphQLQuery = buildGraphQLQuery(op, schema);
      Map<String, String> argumentMapping = buildArgumentMapping(op);
      String domainGroup = buildDomainGroup(op, schema);
      if (!shouldIncludeDomain(domainGroup)) {
        continue;
      }
      if (calculateArgumentComplexity(op, schema) > config.maxArgumentComplexity()) {
        continue;
      }
      List<String> tags = mergeTags(mcpTool.tags(), domainGroup);
      ToolDescriptor.SemanticHints semanticHints = buildSemanticHints(op, domainGroup);

      result.add(
          new ToolDescriptor(
              mcpTool.name(),
              mcpTool.description(),
              mcpTool.category(),
              tags,
              inputSchema,
              graphQLQuery,
              mcpTool.graphQLFieldName(),
              mcpTool.operationType(),
              argumentMapping,
              domainGroup,
              semanticHints));
    }
    return List.copyOf(result);
  }

  private boolean shouldIncludeDomain(String domainGroup) {
    if (!config.includedDomains().isEmpty()
        && config.includedDomains().stream()
            .noneMatch(domain -> domain.equalsIgnoreCase(domainGroup))) {
      return false;
    }

    return config.excludedDomains().stream()
        .noneMatch(domain -> domain.equalsIgnoreCase(domainGroup));
  }

  private Map<String, Object> buildInputSchema(CanonicalOperation op) {
    Map<String, Object> schema = new LinkedHashMap<>();
    schema.put("type", "object");

    Map<String, Object> properties = new LinkedHashMap<>();
    List<String> required = new ArrayList<>();

    for (CanonicalArgument arg : op.arguments()) {
      Map<String, Object> prop = new LinkedHashMap<>();
      prop.put("type", "string");
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

  private String buildDomainGroup(CanonicalOperation op, GraphQLSchema schema) {
    GraphQLObjectType rootType =
        op.operationType() == OperationType.QUERY
            ? schema.getQueryType()
            : schema.getMutationType();
    GraphQLFieldDefinition fieldDef =
        rootType != null ? rootType.getFieldDefinition(op.graphQLFieldName()) : null;

    if (fieldDef != null) {
      GraphQLType unwrapped = GraphQLTypeUtil.unwrapAll(fieldDef.getType());
      if (unwrapped instanceof GraphQLObjectType
          || unwrapped instanceof GraphQLInterfaceType
          || unwrapped instanceof GraphQLUnionType) {
        String fromType = inferDomainGroup(((GraphQLNamedType) unwrapped).getName());
        if (!GENERAL_DOMAIN.equals(fromType)) {
          return fromType;
        }
      }
    }

    String fromField = inferDomainGroup(op.graphQLFieldName());
    if (!GENERAL_DOMAIN.equals(fromField)) {
      return fromField;
    }

    return GENERAL_DOMAIN;
  }

  private int calculateArgumentComplexity(CanonicalOperation op, GraphQLSchema schema) {
    GraphQLObjectType rootType =
        op.operationType() == OperationType.QUERY
            ? schema.getQueryType()
            : schema.getMutationType();
    GraphQLFieldDefinition fieldDef =
        rootType != null ? rootType.getFieldDefinition(op.graphQLFieldName()) : null;

    if (fieldDef == null) {
      return op.arguments().size();
    }

    int complexity = 0;
    for (GraphQLArgument argument : fieldDef.getArguments()) {
      complexity += 1 + calculateTypeComplexity(argument.getType());
    }
    return complexity;
  }

  private int calculateTypeComplexity(GraphQLInputType type) {
    if (type instanceof GraphQLNonNull nonNull) {
      return 1 + calculateTypeComplexity((GraphQLInputType) nonNull.getWrappedType());
    }

    if (type instanceof GraphQLList listType) {
      return 2 + calculateTypeComplexity((GraphQLInputType) listType.getWrappedType());
    }

    if (type instanceof GraphQLInputObjectType inputObjectType) {
      int nested = 0;
      for (GraphQLInputObjectField field : inputObjectType.getFields()) {
        nested += 1 + calculateTypeComplexity(field.getType());
      }
      return 4 + nested;
    }

    return 1;
  }

  private List<String> mergeTags(List<String> tags, String domainGroup) {
    Set<String> merged = new TreeSet<>(String.CASE_INSENSITIVE_ORDER);
    merged.addAll(tags);
    if (domainGroup != null && !domainGroup.isBlank()) {
      merged.add(domainGroup);
    }
    return List.copyOf(merged);
  }

  private String inferDomainGroup(String value) {
    List<String> tokens = meaningfulTokens(value);
    if (tokens.isEmpty()) {
      return GENERAL_DOMAIN;
    }

    for (int index = tokens.size() - 1; index >= 0; index--) {
      String token = tokens.get(index);
      if (!GENERIC_DOMAIN_TOKENS.contains(token)) {
        return singularize(token);
      }
    }

    return GENERAL_DOMAIN;
  }

  private ToolDescriptor.SemanticHints buildSemanticHints(
      CanonicalOperation op, String domainGroup) {
    String intent = inferIntent(op);
    List<String> keywords = buildKeywords(op, domainGroup);
    return new ToolDescriptor.SemanticHints(intent, keywords);
  }

  private String inferIntent(CanonicalOperation op) {
    String action = firstActionToken(op.graphQLFieldName());
    if (action == null && op.description() != null) {
      action = firstActionToken(op.description());
    }

    if (action != null) {
      return switch (action) {
        case "list" -> "list";
        case "search" -> "search";
        case "count" -> "count";
        case "create", "add", "new" -> "create";
        case "update", "set", "modify", "patch", "edit" -> "update";
        case "delete", "remove", "archive", "drop" -> "delete";
        case "upsert", "replace" -> "upsert";
        case "get", "fetch", "find", "lookup", "read", "show", "load", "view", "return" ->
            "retrieve";
        default -> defaultIntent(op.operationType());
      };
    }

    return defaultIntent(op.operationType());
  }

  private String defaultIntent(OperationType type) {
    return type == OperationType.QUERY ? "retrieve" : "write";
  }

  private String firstActionToken(String value) {
    List<String> tokens = splitIdentifier(value);
    if (tokens.isEmpty()) {
      return null;
    }

    String normalized = normalizeToken(tokens.get(0));
    return ACTION_PREFIXES.contains(normalized) ? normalized : null;
  }

  private List<String> buildKeywords(CanonicalOperation op, String domainGroup) {
    Set<String> keywords = new LinkedHashSet<>();
    addKeywords(keywords, domainGroup);
    addKeywords(keywords, op.operationType().name().toLowerCase(Locale.ROOT));
    addKeywords(keywords, op.graphQLFieldName());
    for (CanonicalArgument arg : op.arguments()) {
      addKeywords(keywords, arg.name());
    }
    return List.copyOf(keywords);
  }

  private void addKeywords(Set<String> keywords, String value) {
    for (String token : keywordTokens(value)) {
      if (!token.isBlank() && !GENERAL_DOMAIN.equals(token)) {
        keywords.add(token);
      }
    }
  }

  private List<String> keywordTokens(String value) {
    List<String> tokens = new ArrayList<>();
    for (String token : splitIdentifier(value)) {
      String normalized = normalizeToken(token);
      if (normalized.isBlank()
          || ACTION_PREFIXES.contains(normalized)
          || STOP_WORDS.contains(normalized)
          || STRUCTURAL_SUFFIXES.contains(normalized)) {
        continue;
      }
      tokens.add(singularize(normalized));
    }
    return tokens;
  }

  private List<String> meaningfulTokens(String value) {
    List<String> tokens = new ArrayList<>();
    for (String token : splitIdentifier(value)) {
      String normalized = normalizeToken(token);
      if (!normalized.isBlank()) {
        tokens.add(normalized);
      }
    }

    while (!tokens.isEmpty() && ACTION_PREFIXES.contains(tokens.get(0))) {
      tokens.remove(0);
    }

    int stopIndex = tokens.size();
    for (int index = 0; index < tokens.size(); index++) {
      if (STOP_WORDS.contains(tokens.get(index))) {
        stopIndex = index;
        break;
      }
    }
    tokens = new ArrayList<>(tokens.subList(0, stopIndex));

    while (!tokens.isEmpty() && STRUCTURAL_SUFFIXES.contains(tokens.get(tokens.size() - 1))) {
      tokens.remove(tokens.size() - 1);
    }

    return tokens;
  }

  private List<String> splitIdentifier(String value) {
    List<String> tokens = new ArrayList<>();
    if (value == null || value.isBlank()) {
      return tokens;
    }

    StringBuilder current = new StringBuilder();
    Runnable flush =
        () -> {
          if (!current.isEmpty()) {
            tokens.add(current.toString());
            current.setLength(0);
          }
        };

    for (int index = 0; index < value.length(); index++) {
      char c = value.charAt(index);
      if (!Character.isLetterOrDigit(c)) {
        flush.run();
        continue;
      }

      if (!current.isEmpty()) {
        char previous = current.charAt(current.length() - 1);
        char next = index + 1 < value.length() ? value.charAt(index + 1) : '\0';
        boolean boundary =
            (Character.isLowerCase(previous) && Character.isUpperCase(c))
                || (Character.isLetter(previous) && Character.isDigit(c))
                || (Character.isDigit(previous) && Character.isLetter(c))
                || (Character.isUpperCase(previous)
                    && Character.isUpperCase(c)
                    && Character.isLowerCase(next));

        if (boundary) {
          flush.run();
        }
      }

      current.append(c);
    }

    flush.run();
    return tokens;
  }

  private String normalizeToken(String token) {
    return token == null ? "" : token.toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9]", "");
  }

  private String singularize(String token) {
    if (token.length() <= 3) {
      return token;
    }
    if (token.endsWith("ies")) {
      return token.substring(0, token.length() - 3) + "y";
    }
    if (token.endsWith("sses")
        || token.endsWith("shes")
        || token.endsWith("ches")
        || token.endsWith("xes")
        || token.endsWith("zes")) {
      return token.substring(0, token.length() - 2);
    }
    if (token.endsWith("ses") && !token.endsWith("uses")) {
      return token.substring(0, token.length() - 2);
    }
    if (token.endsWith("s")
        && !token.endsWith("ss")
        && !token.endsWith("us")
        && !token.endsWith("is")) {
      return token.substring(0, token.length() - 1);
    }
    return token;
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
      if (visited.contains(objType.getName())) {
        return "";
      }
      Set<String> nextVisited = new HashSet<>(visited);
      nextVisited.add(objType.getName());

      List<String> fields = new ArrayList<>();
      for (GraphQLFieldDefinition field : objType.getFieldDefinitions()) {
        if (field.getName().startsWith("__")) {
          continue;
        }
        if (config.excludedFields().contains(field.getName())) {
          continue;
        }

        String inner = buildSelectionSet(field.getType(), depth - 1, nextVisited);
        fields.add(field.getName() + inner);
      }
      return fields.isEmpty() ? "" : " { " + String.join(" ", fields) + " }";
    }

    if (unwrapped instanceof GraphQLInterfaceType ifaceType) {
      if (visited.contains(ifaceType.getName())) {
        return "";
      }
      Set<String> nextVisited = new HashSet<>(visited);
      nextVisited.add(ifaceType.getName());

      List<String> fields = new ArrayList<>();
      fields.add("__typename");
      for (GraphQLFieldDefinition field : ifaceType.getFieldDefinitions()) {
        if (field.getName().startsWith("__")) {
          continue;
        }
        if (config.excludedFields().contains(field.getName())) {
          continue;
        }

        String inner = buildSelectionSet(field.getType(), depth - 1, nextVisited);
        fields.add(field.getName() + inner);
      }
      return fields.isEmpty() ? "" : " { " + String.join(" ", fields) + " }";
    }

    if (unwrapped instanceof GraphQLUnionType unionType) {
      if (visited.contains(unionType.getName())) {
        return "";
      }
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
