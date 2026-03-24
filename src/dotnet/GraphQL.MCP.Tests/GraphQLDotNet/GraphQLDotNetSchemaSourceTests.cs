using FluentAssertions;
using GraphQL.MCP.Abstractions.Canonical;
using GraphQL.MCP.GraphQLDotNet;
using GraphQL.Types;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using CanonicalTypeKind = GraphQL.MCP.Abstractions.Canonical.TypeKind;

namespace GraphQL.MCP.Tests.GraphQLDotNet;

public class GraphQLDotNetSchemaSourceTests
{
    [Fact]
    public async Task Should_extract_query_operations()
    {
        var schema = CreateBookSchema();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var operations = await sut.GetOperationsAsync();

        operations.Should().HaveCount(3);
        operations.Select(o => o.Name).Should().Contain(["hello", "books", "bookByTitle"]);
        operations.Should().OnlyContain(o => o.OperationType == OperationType.Query);
    }

    [Fact]
    public async Task Should_extract_mutation_operations()
    {
        var schema = CreateSchemaWithMutations();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var operations = await sut.GetOperationsAsync();

        var addBook = operations.Single(o => o.Name == "addBook");
        addBook.OperationType.Should().Be(OperationType.Mutation);
        addBook.Arguments.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task Should_map_scalar_return_types()
    {
        var schema = CreateBookSchema();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var operations = await sut.GetOperationsAsync();
        var hello = operations.Single(o => o.Name == "hello");

        var returnType = hello.ReturnType;
        returnType.Kind.Should().Be(CanonicalTypeKind.Scalar);
        returnType.Name.Should().Be("String");
    }

    [Fact]
    public async Task Should_map_list_return_types()
    {
        var schema = CreateBookSchema();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var operations = await sut.GetOperationsAsync();
        var books = operations.Single(o => o.Name == "books");

        var returnType = books.ReturnType;
        returnType.Kind.Should().Be(CanonicalTypeKind.List);
        returnType.IsList.Should().BeTrue();
        returnType.OfType.Should().NotBeNull();
        returnType.OfType!.Name.Should().Be("Book");
        returnType.OfType.Kind.Should().Be(CanonicalTypeKind.Object);
    }

    [Fact]
    public async Task Should_map_object_fields()
    {
        var schema = CreateBookSchema();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var operations = await sut.GetOperationsAsync();
        var bookByTitle = operations.Single(o => o.Name == "bookByTitle");

        var returnType = bookByTitle.ReturnType;
        returnType.Kind.Should().Be(CanonicalTypeKind.Object);
        returnType.Fields.Should().NotBeNullOrEmpty();
        returnType.Fields!.Select(f => f.Name).Should().Contain(["title", "author"]);
    }

    [Fact]
    public async Task Should_map_nested_object_fields()
    {
        var schema = CreateBookSchema();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var operations = await sut.GetOperationsAsync();
        var bookByTitle = operations.Single(o => o.Name == "bookByTitle");

        var authorField = bookByTitle.ReturnType.Fields!.Single(f => f.Name == "author");
        var authorType = authorField.Type;
        authorType.Kind.Should().Be(CanonicalTypeKind.Object);
        authorType.Fields.Should().NotBeNullOrEmpty();
        authorType.Fields!.Select(f => f.Name).Should().Contain(["name", "email"]);
    }

    [Fact]
    public async Task Should_map_arguments_with_defaults()
    {
        var schema = CreateBookSchema();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var operations = await sut.GetOperationsAsync();
        var hello = operations.Single(o => o.Name == "hello");

        hello.Arguments.Should().HaveCount(1);
        var nameArg = hello.Arguments.Single(a => a.Name == "name");
        nameArg.Type.Kind.Should().Be(CanonicalTypeKind.Scalar);
        nameArg.Type.Name.Should().Be("String");
        nameArg.IsRequired.Should().BeFalse();
        nameArg.DefaultValue.Should().Be("World");
    }

    [Fact]
    public async Task Should_map_required_arguments()
    {
        var schema = CreateBookSchema();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var operations = await sut.GetOperationsAsync();
        var bookByTitle = operations.Single(o => o.Name == "bookByTitle");

        bookByTitle.Arguments.Should().HaveCount(1);
        var titleArg = bookByTitle.Arguments.Single(a => a.Name == "title");
        titleArg.IsRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Should_skip_introspection_fields()
    {
        var schema = CreateBookSchema();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var operations = await sut.GetOperationsAsync();

        operations.Should().NotContain(o => o.Name.StartsWith("__"));
    }

    [Fact]
    public async Task Should_extract_all_types()
    {
        var schema = CreateBookSchema();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var types = await sut.GetTypesAsync();

        types.Should().ContainKey("Book");
        types.Should().ContainKey("Author");
        types.Should().ContainKey("String");
        types.Should().NotContainKey("__Schema");
        types.Should().NotContainKey("__Type");
    }

    [Fact]
    public async Task Should_preserve_fields_for_repeated_sibling_object_types()
    {
        var schema = CreateCheckoutSchema();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var operations = await sut.GetOperationsAsync();
        var checkout = operations.Single(op => op.GraphQLFieldName == "checkout");
        var returnType = checkout.ReturnType;
        var checkoutFields = returnType.Fields!.ToDictionary(f => f.Name);

        var shippingType = checkoutFields["shippingAddress"].Type;
        var shippingFields = shippingType.Fields!;

        var billingType = checkoutFields["billingAddress"].Type;
        var billingFields = billingType.Fields!;

        shippingFields.Select(f => f.Name).Should().Contain(["street", "city"]);
        billingFields.Select(f => f.Name).Should().Contain(["street", "city"]);
    }

    [Fact]
    public async Task Should_handle_enum_types()
    {
        var schema = CreateSchemaWithEnum();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var types = await sut.GetTypesAsync();

        types.Should().ContainKey("Category");
        var categoryType = types["Category"];
        categoryType.Kind.Should().Be(CanonicalTypeKind.Enum);
        categoryType.EnumValues.Should().Contain(["FICTION", "NON_FICTION", "TECHNICAL"]);
    }

    [Fact]
    public async Task Should_handle_interface_types()
    {
        var schema = CreateSchemaWithInterface();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var types = await sut.GetTypesAsync();

        types.Should().ContainKey("Node");
        var nodeType = types["Node"];
        nodeType.Kind.Should().Be(CanonicalTypeKind.Interface);
        nodeType.Fields.Should().NotBeNullOrEmpty();
        nodeType.Fields!.Select(f => f.Name).Should().Contain("id");
    }

    [Fact]
    public async Task Should_handle_union_types()
    {
        var schema = CreateSchemaWithUnion();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var types = await sut.GetTypesAsync();

        types.Should().ContainKey("SearchResult");
        var unionType = types["SearchResult"];
        unionType.Kind.Should().Be(CanonicalTypeKind.Union);
        unionType.PossibleTypes.Should().NotBeNullOrEmpty();
        unionType.PossibleTypes!.Select(t => t.Name).Should().Contain(["BookResult", "AuthorResult"]);
    }

    [Fact]
    public async Task Should_handle_input_object_types()
    {
        var schema = CreateSchemaWithMutations();
        var sut = new GraphQLDotNetSchemaSource(schema, NullLogger<GraphQLDotNetSchemaSource>.Instance);

        var types = await sut.GetTypesAsync();

        types.Should().ContainKey("BookInput");
        var inputType = types["BookInput"];
        inputType.Kind.Should().Be(CanonicalTypeKind.InputObject);
        inputType.Fields.Should().NotBeNullOrEmpty();
        inputType.Fields!.Select(f => f.Name).Should().Contain(["title", "authorName"]);
    }

    // --- Schema Factories ---

    private static ISchema CreateBookSchema()
    {
        var schema = Schema.For(@"
            type Query {
                hello(name: String = ""World""): String
                books: [Book]
                bookByTitle(title: String!): Book
            }

            type Book {
                title: String
                author: Author
            }

            type Author {
                name: String
                email: String
            }
        ", builder =>
        {
            builder.Types.Include<QueryType>();
        });
        schema.Initialize();
        return schema;
    }

    private static ISchema CreateCheckoutSchema()
    {
        var schema = Schema.For(@"
            type Query {
                checkout: Checkout
            }

            type Checkout {
                shippingAddress: Address
                billingAddress: Address
            }

            type Address {
                street: String
                city: String
            }
        ");
        schema.Initialize();
        return schema;
    }

    private static ISchema CreateSchemaWithEnum()
    {
        var schema = Schema.For(@"
            type Query {
                books: [Book]
            }

            type Book {
                title: String
                category: Category
            }

            enum Category {
                FICTION
                NON_FICTION
                TECHNICAL
            }
        ");
        schema.Initialize();
        return schema;
    }

    private static ISchema CreateSchemaWithInterface()
    {
        var schema = Schema.For(@"
            type Query {
                node(id: ID!): Node
            }

            interface Node {
                id: ID!
            }

            type BookNode implements Node {
                id: ID!
                title: String
            }
        ", builder =>
        {
            builder.Types.For("BookNode").IsTypeOf<object>();
        });
        schema.Initialize();
        return schema;
    }

    private static ISchema CreateSchemaWithUnion()
    {
        var schema = Schema.For(@"
            type Query {
                search(query: String!): [SearchResult]
            }

            union SearchResult = BookResult | AuthorResult

            type BookResult {
                title: String
            }

            type AuthorResult {
                name: String
            }
        ", builder =>
        {
            builder.Types.For("BookResult").IsTypeOf<object>();
            builder.Types.For("AuthorResult").IsTypeOf<object>();
        });
        schema.Initialize();
        return schema;
    }

    private static ISchema CreateSchemaWithMutations()
    {
        var schema = Schema.For(@"
            type Query {
                books: [Book]
            }

            type Mutation {
                addBook(input: BookInput!): Book
            }

            type Book {
                title: String
                authorName: String
            }

            input BookInput {
                title: String!
                authorName: String!
            }
        ");
        schema.Initialize();
        return schema;
    }

    [GraphQLMetadata("Query")]
    private class QueryType
    {
        public string Hello(string name = "World") => $"Hello, {name}!";
        public object[] Books() => [];
        public object? BookByTitle(string title) => null;
    }
}
