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

    // --- Argument descriptions ---

    [Fact]
    public void Should_include_argument_description_in_schema()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("userById", args:
        [
            new CanonicalArgument
            {
                Name = "id",
                Description = "The unique user identifier",
                Type = new CanonicalType { Name = "ID", Kind = TypeKind.Scalar },
                IsRequired = true
            }
        ]);

        var tools = sut.Publish([op]);
        var idProp = tools[0].InputSchema.RootElement
            .GetProperty("properties")
            .GetProperty("id");

        idProp.GetProperty("description").GetString().Should().Be("The unique user identifier");
    }

    // --- Default values ---

    [Fact]
    public void Should_include_default_value_in_schema()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("users", args:
        [
            new CanonicalArgument
            {
                Name = "limit",
                Type = new CanonicalType { Name = "Int", Kind = TypeKind.Scalar },
                DefaultValue = 10
            }
        ]);

        var tools = sut.Publish([op]);
        var limitProp = tools[0].InputSchema.RootElement
            .GetProperty("properties")
            .GetProperty("limit");

        limitProp.GetProperty("default").GetInt32().Should().Be(10);
    }

    [Fact]
    public void Should_include_string_default_value()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("users", args:
        [
            new CanonicalArgument
            {
                Name = "sortBy",
                Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar },
                DefaultValue = "name"
            }
        ]);

        var tools = sut.Publish([op]);
        var prop = tools[0].InputSchema.RootElement
            .GetProperty("properties")
            .GetProperty("sortBy");

        prop.GetProperty("default").GetString().Should().Be("name");
    }

    // --- InputObject required fields ---

    [Fact]
    public void Should_detect_required_fields_in_InputObject()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("createUser", args:
        [
            new CanonicalArgument
            {
                Name = "input",
                Type = new CanonicalType
                {
                    Name = "CreateUserInput",
                    Kind = TypeKind.InputObject,
                    Fields =
                    [
                        new CanonicalField
                        {
                            Name = "email",
                            Type = new CanonicalType
                            {
                                Name = "String",
                                Kind = TypeKind.NonNull,
                                IsNonNull = true,
                                OfType = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
                            }
                        },
                        new CanonicalField
                        {
                            Name = "nickname",
                            Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
                        }
                    ]
                },
                IsRequired = true
            }
        ]);

        var tools = sut.Publish([op]);
        var inputProp = tools[0].InputSchema.RootElement
            .GetProperty("properties")
            .GetProperty("input");

        inputProp.GetProperty("type").GetString().Should().Be("object");
        inputProp.GetProperty("required").GetArrayLength().Should().Be(1);
        inputProp.GetProperty("required")[0].GetString().Should().Be("email");
    }

    // --- Depth truncation ---

    [Fact]
    public void Should_truncate_selection_set_at_MaxOutputDepth()
    {
        var sut = CreateSut(new McpOptions { MaxOutputDepth = 1 });

        var nestedType = new CanonicalType
        {
            Name = "Address",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "city", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } }
            ]
        };

        var userType = new CanonicalType
        {
            Name = "User",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "name", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } },
                new CanonicalField { Name = "address", Type = nestedType }
            ]
        };

        var op = new CanonicalOperation
        {
            Name = "users",
            GraphQLFieldName = "users",
            OperationType = OperationType.Query,
            ReturnType = userType
        };

        var tools = sut.Publish([op]);

        // At depth 1, should include scalar fields but not nested object fields
        tools[0].GraphQLQuery.Should().Contain("name");
        tools[0].GraphQLQuery.Should().NotContain("address");
    }

    [Fact]
    public void Should_include_nested_fields_at_sufficient_depth()
    {
        var sut = CreateSut(new McpOptions { MaxOutputDepth = 3 });

        var nestedType = new CanonicalType
        {
            Name = "Address",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "city", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } }
            ]
        };

        var userType = new CanonicalType
        {
            Name = "User",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "name", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } },
                new CanonicalField { Name = "address", Type = nestedType }
            ]
        };

        var op = new CanonicalOperation
        {
            Name = "users",
            GraphQLFieldName = "users",
            OperationType = OperationType.Query,
            ReturnType = userType
        };

        var tools = sut.Publish([op]);

        tools[0].GraphQLQuery.Should().Contain("name");
        tools[0].GraphQLQuery.Should().Contain("address");
        tools[0].GraphQLQuery.Should().Contain("city");
    }

    // --- Circular type protection ---

    [Fact]
    public void Should_handle_circular_types_without_stack_overflow()
    {
        var sut = CreateSut(new McpOptions { MaxOutputDepth = 5 });

        // Create a circular type: User -> friends: [User]
        var userType = new CanonicalType
        {
            Name = "User",
            Kind = TypeKind.Object,
            Fields = new List<CanonicalField>()
        };

        // Circular reference
        var fields = new List<CanonicalField>
        {
            new() { Name = "id", Type = new CanonicalType { Name = "ID", Kind = TypeKind.Scalar } },
            new() { Name = "name", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } },
            new()
            {
                Name = "friends",
                Type = new CanonicalType
                {
                    Name = "User",
                    Kind = TypeKind.List,
                    IsList = true,
                    OfType = userType
                }
            }
        };

        // Use reflection to set Fields since it's init-only
        var fieldsProperty = typeof(CanonicalType).GetProperty(nameof(CanonicalType.Fields))!;
        fieldsProperty.SetValue(userType, fields.AsReadOnly());

        var op = new CanonicalOperation
        {
            Name = "users",
            GraphQLFieldName = "users",
            OperationType = OperationType.Query,
            ReturnType = userType
        };

        // Should not throw StackOverflowException
        var tools = sut.Publish([op]);

        tools.Should().HaveCount(1);
        tools[0].GraphQLQuery.Should().Contain("id");
        tools[0].GraphQLQuery.Should().Contain("name");
    }

    // --- Union/Interface types ---

    [Fact]
    public void Should_generate_inline_fragments_for_union_types()
    {
        var sut = CreateSut(new McpOptions { MaxOutputDepth = 3 });

        var dogType = new CanonicalType
        {
            Name = "Dog",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "breed", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } }
            ]
        };

        var catType = new CanonicalType
        {
            Name = "Cat",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "color", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } }
            ]
        };

        var animalUnion = new CanonicalType
        {
            Name = "Animal",
            Kind = TypeKind.Union,
            PossibleTypes = [dogType, catType]
        };

        var op = new CanonicalOperation
        {
            Name = "animals",
            GraphQLFieldName = "animals",
            OperationType = OperationType.Query,
            ReturnType = animalUnion
        };

        var tools = sut.Publish([op]);

        tools[0].GraphQLQuery.Should().Contain("__typename");
        tools[0].GraphQLQuery.Should().Contain("... on Dog");
        tools[0].GraphQLQuery.Should().Contain("... on Cat");
        tools[0].GraphQLQuery.Should().Contain("breed");
        tools[0].GraphQLQuery.Should().Contain("color");
    }

    [Fact]
    public void Should_generate_typename_for_interface_without_possible_types()
    {
        var sut = CreateSut();

        var interfaceType = new CanonicalType
        {
            Name = "Node",
            Kind = TypeKind.Interface,
            PossibleTypes = null
        };

        var op = new CanonicalOperation
        {
            Name = "nodes",
            GraphQLFieldName = "nodes",
            OperationType = OperationType.Query,
            ReturnType = interfaceType
        };

        var tools = sut.Publish([op]);

        tools[0].GraphQLQuery.Should().Contain("__typename");
    }

    // --- No arguments ---

    [Fact]
    public void Should_generate_query_without_variables_for_no_args()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("healthCheck");

        var tools = sut.Publish([op]);

        tools[0].GraphQLQuery.Should().Be("query McpOperation { healthCheck }");
        tools[0].InputSchema.RootElement.GetProperty("properties")
            .EnumerateObject().Should().BeEmpty();
    }

    // --- Custom scalars ---

    [Fact]
    public void Should_map_custom_scalar_to_string()
    {
        var sut = CreateSut();
        var op = CreateQueryOp("events", args:
        [
            new CanonicalArgument
            {
                Name = "after",
                Type = new CanonicalType { Name = "DateTime", Kind = TypeKind.Scalar }
            }
        ]);

        var tools = sut.Publish([op]);
        var prop = tools[0].InputSchema.RootElement
            .GetProperty("properties")
            .GetProperty("after");

        prop.GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void Should_throw_when_multiple_operations_share_a_tool_name()
    {
        var sut = CreateSut(new McpOptions { AllowMutations = true });
        var ops = new List<CanonicalOperation>
        {
            CreateQueryOp("users"),
            new()
            {
                Name = "get_users",
                GraphQLFieldName = "get_users",
                Description = "Mutation that collides with query naming",
                OperationType = OperationType.Mutation,
                ReturnType = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
            }
        };

        var act = () => sut.Publish(ops);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*same MCP tool name 'get_users'*");
    }
}
