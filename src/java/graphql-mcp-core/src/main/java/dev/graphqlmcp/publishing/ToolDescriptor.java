package dev.graphqlmcp.publishing;

import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector.OperationType;
import java.util.Map;

/** A fully-resolved MCP tool descriptor ready for serving over the MCP protocol. */
public record ToolDescriptor(
    String name,
    String description,
    String category,
    java.util.List<String> tags,
    Map<String, Object> inputSchema,
    String graphQLQuery,
    String graphQLFieldName,
    OperationType operationType,
    Map<String, String> argumentMapping,
    String domainGroup) {}
