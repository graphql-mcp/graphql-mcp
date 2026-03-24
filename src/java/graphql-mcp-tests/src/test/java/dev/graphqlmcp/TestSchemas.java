package dev.graphqlmcp;

import graphql.Scalars;
import graphql.schema.GraphQLArgument;
import graphql.schema.GraphQLNonNull;
import graphql.schema.GraphQLObjectType;
import graphql.schema.GraphQLSchema;
import java.util.Map;

/** Shared GraphQL schemas used by Java tests. */
public final class TestSchemas {

  private TestSchemas() {}

  public static GraphQLSchema createSchema() {
    GraphQLObjectType authorType =
        GraphQLObjectType.newObject()
            .name("Author")
            .field(field -> field.name("name").type(GraphQLNonNull.nonNull(Scalars.GraphQLString)))
            .field(field -> field.name("email").type(Scalars.GraphQLString))
            .build();

    GraphQLObjectType bookType =
        GraphQLObjectType.newObject()
            .name("Book")
            .field(field -> field.name("title").type(Scalars.GraphQLString))
            .field(field -> field.name("secretNote").type(Scalars.GraphQLString))
            .field(field -> field.name("author").type(authorType))
            .build();

    GraphQLObjectType queryType =
        GraphQLObjectType.newObject()
            .name("Query")
            .field(
                field ->
                    field
                        .name("hello")
                        .description("Return a greeting")
                        .type(Scalars.GraphQLString)
                        .argument(
                            GraphQLArgument.newArgument()
                                .name("name")
                                .description("Name to greet")
                                .type(GraphQLNonNull.nonNull(Scalars.GraphQLString)))
                        .dataFetcher(
                            env -> {
                              Object auth = env.getGraphQlContext().get("Authorization");
                              String name = env.getArgument("name");
                              return "Hello, "
                                  + name
                                  + " | auth="
                                  + (auth == null ? "none" : auth.toString());
                            }))
            .field(
                field ->
                    field
                        .name("book")
                        .description("Find a book")
                        .type(bookType)
                        .argument(
                            GraphQLArgument.newArgument()
                                .name("id")
                                .description("Book identifier")
                                .type(GraphQLNonNull.nonNull(Scalars.GraphQLID)))
                        .dataFetcher(
                            env ->
                                Map.of(
                                    "title",
                                    "The GraphQL Guide",
                                    "secretNote",
                                    "internal",
                                    "author",
                                    Map.of("name", "Ada", "email", "ada@example.com"))))
            .build();

    GraphQLObjectType mutationType =
        GraphQLObjectType.newObject()
            .name("Mutation")
            .field(
                field ->
                    field
                        .name("createBook")
                        .description("Create a book")
                        .type(bookType)
                        .argument(
                            GraphQLArgument.newArgument()
                                .name("title")
                                .description("Title")
                                .type(GraphQLNonNull.nonNull(Scalars.GraphQLString)))
                        .dataFetcher(
                            env ->
                                Map.of(
                                    "title",
                                    env.getArgument("title"),
                                    "secretNote",
                                    "created",
                                    "author",
                                    Map.of("name", "System", "email", "system@example.com"))))
            .build();

    return GraphQLSchema.newSchema()
        .query(queryType)
        .mutation(mutationType)
        .additionalType(authorType)
        .additionalType(bookType)
        .build();
  }

  public static GraphQLSchema createExecutionSchema() {
    return createSchema();
  }
}
