using FluentAssertions;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using GraphQL.MCP.Abstractions.Policy;
using GraphQL.MCP.Core.Policy;
using GraphQL.MCP.Core.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GraphQL.MCP.Tests.Publishing;

public class ToolPublisherTests
{
    private static ToolPublisher CreateSut(McpOptions? options = null)
    {
        var opts = options ?? new McpOptions();
        var policy = new PolicyEngine(Options.Create(opts), NullLogger<PolicyEngine>.Instance);
        return new ToolPublisher(
            policy,
            Options.Create(opts),
            NullLogger<ToolPublisher>.Instance);
    }

    private static CanonicalOperation CreateQueryOp(
        string name,
        string? description = null,
        List<CanonicalArgument>? args = null) => new()
    {
        Name = name,
        GraphQLFieldName = name,
        Description = description,
        OperationType = OperationType.Query,
        Arguments = args ?? [],
        ReturnType = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
    };

    [Fact]
    public void Should_publish_tools_from_operations()
    {
        var sut = CreateSut();
        var ops = new List<CanonicalOperation>
        {
            CreateQueryOp("users", "Get all users"),
            CreateQueryOp("orders", "Get all orders"),
        };

        var tools = sut.Publish(ops);

        tools.Should().HaveCount(2);
        tools[0].Name.Should().Be("get_users");
        tools[1].Name.Should().Be("get_orders");
    }

    [Fact]
    public void Should_include_description_from_graphql()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("users", "Retrieves all users");

        var tools = sut.Publish([op]);

        tools[0].Description.Should().Contain("Retrieves all users");
    }

    [Fact]
    public void Should_prefix_mutations_with_MUTATION_in_description()
    {
        var sut = CreateSut(new McpOptions { AllowMutations = true });
        var op = new CanonicalOperation
        {
            Name = "createUser",
            GraphQLFieldName = "createUser",
            Description = "Creates a user",
            OperationType = OperationType.Mutation,
            ReturnType = new CanonicalType { Name = "User", Kind = TypeKind.Object }
        };

        var tools = sut.Publish([op]);

        tools[0].Description.Should().StartWith("[MUTATION]");
    }

    [Fact]
    public void Should_generate_input_schema_with_required_args()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("userById", args:
        [
            new CanonicalArgument
            {
                Name = "id",
                Type = new CanonicalType { Name = "ID", Kind = TypeKind.Scalar },
                IsRequired = true
            }
        ]);

        var tools = sut.Publish([op]);
        var schema = tools[0].InputSchema.RootElement;

        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("properties").GetProperty("id")
            .GetProperty("type").GetString().Should().Be("string");
        schema.GetProperty("required")[0].GetString().Should().Be("id");
    }

    [Fact]
    public void Should_map_graphql_scalars_to_json_types()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("test", args:
        [
            new CanonicalArgument { Name = "str", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } },
            new CanonicalArgument { Name = "num", Type = new CanonicalType { Name = "Int", Kind = TypeKind.Scalar } },
            new CanonicalArgument { Name = "dec", Type = new CanonicalType { Name = "Float", Kind = TypeKind.Scalar } },
            new CanonicalArgument { Name = "flag", Type = new CanonicalType { Name = "Boolean", Kind = TypeKind.Scalar } },
        ]);

        var tools = sut.Publish([op]);
        var props = tools[0].InputSchema.RootElement.GetProperty("properties");

        props.GetProperty("str").GetProperty("type").GetString().Should().Be("string");
        props.GetProperty("num").GetProperty("type").GetString().Should().Be("integer");
        props.GetProperty("dec").GetProperty("type").GetString().Should().Be("number");
        props.GetProperty("flag").GetProperty("type").GetString().Should().Be("boolean");
    }

    [Fact]
    public void Should_generate_graphql_query_string()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("users");

        var tools = sut.Publish([op]);

        tools[0].GraphQLQuery.Should().StartWith("query McpOperation");
        tools[0].GraphQLQuery.Should().Contain("users");
    }

    [Fact]
    public void Should_include_variables_in_query()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("userById", args:
        [
            new CanonicalArgument
            {
                Name = "id",
                Type = new CanonicalType
                {
                    Name = "ID",
                    Kind = TypeKind.NonNull,
                    IsNonNull = true,
                    OfType = new CanonicalType { Name = "ID", Kind = TypeKind.Scalar }
                },
                IsRequired = true
            }
        ]);

        var tools = sut.Publish([op]);

        tools[0].GraphQLQuery.Should().Contain("$id: ID!");
        tools[0].GraphQLQuery.Should().Contain("userById(id: $id)");
    }

    [Fact]
    public void Should_handle_enum_arguments()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("usersByStatus", args:
        [
            new CanonicalArgument
            {
                Name = "status",
                Type = new CanonicalType
                {
                    Name = "UserStatus",
                    Kind = TypeKind.Enum,
                    EnumValues = ["ACTIVE", "INACTIVE", "BANNED"]
                }
            }
        ]);

        var tools = sut.Publish([op]);
        var statusProp = tools[0].InputSchema.RootElement
            .GetProperty("properties")
            .GetProperty("status");

        statusProp.GetProperty("type").GetString().Should().Be("string");
        statusProp.GetProperty("enum").GetArrayLength().Should().Be(3);
    }
}
