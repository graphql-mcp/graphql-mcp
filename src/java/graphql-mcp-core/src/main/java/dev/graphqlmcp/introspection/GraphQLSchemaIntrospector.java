package dev.graphqlmcp.introspection;

import graphql.schema.*;

import java.util.*;

/**
 * Introspects a GraphQL schema and extracts canonical operations.
 * This is the Java equivalent of SchemaCanonicalizer in .NET.
 */
public class GraphQLSchemaIntrospector {

    /**
     * Extracts all root-level query and mutation fields from the schema.
     */
    public List<CanonicalOperation> introspect(GraphQLSchema schema) {
        List<CanonicalOperation> operations = new ArrayList<>();

        GraphQLObjectType queryType = schema.getQueryType();
        if (queryType != null) {
            for (GraphQLFieldDefinition field : queryType.getFieldDefinitions()) {
                if (!field.getName().startsWith("__")) {
                    operations.add(mapField(field, OperationType.QUERY));
                }
            }
        }

        GraphQLObjectType mutationType = schema.getMutationType();
        if (mutationType != null) {
            for (GraphQLFieldDefinition field : mutationType.getFieldDefinitions()) {
                if (!field.getName().startsWith("__")) {
                    operations.add(mapField(field, OperationType.MUTATION));
                }
            }
        }

        return Collections.unmodifiableList(operations);
    }

    private CanonicalOperation mapField(GraphQLFieldDefinition field, OperationType type) {
        List<CanonicalArgument> args = field.getArguments().stream()
                .map(this::mapArgument)
                .toList();

        return new CanonicalOperation(
                field.getName(),
                field.getDescription(),
                type,
                field.getName(),
                args
        );
    }

    private CanonicalArgument mapArgument(GraphQLArgument arg) {
        return new CanonicalArgument(
                arg.getName(),
                arg.getDescription(),
                arg.getType() instanceof GraphQLNonNull,
                arg.getArgumentDefaultValue().isSet() ? arg.getArgumentDefaultValue().getValue() : null
        );
    }

    public enum OperationType {
        QUERY, MUTATION
    }

    public record CanonicalOperation(
            String name,
            String description,
            OperationType operationType,
            String graphQLFieldName,
            List<CanonicalArgument> arguments
    ) {}

    public record CanonicalArgument(
            String name,
            String description,
            boolean required,
            Object defaultValue
    ) {}
}
