using FluentAssertions;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using GraphQL.MCP.Core.Canonical;
using GraphQL.MCP.Core.Policy;
using GraphQL.MCP.Core.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace GraphQL.MCP.Tests.Integration;

/// <summary>
/// Tests the full pipeline: SchemaCanonicalizer → PolicyEngine.Apply → ToolPublisher.Publish
/// </summary>
public class PipelineIntegrationTests
{
    [Fact]
    public async Task Full_pipeline_should_produce_correct_tools_from_schema()
    {
        // Arrange: realistic schema with queries, mutations, introspection, excluded types
        var schemaSource = Substitute.For<IGraphQLSchemaSource>();

        var userType = new CanonicalType
        {
            Name = "User",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "id", Type = new CanonicalType { Name = "ID", Kind = TypeKind.Scalar } },
                new CanonicalField { Name = "name", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } },
                new CanonicalField { Name = "email", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } },
            ]
        };

        var orderType = new CanonicalType
        {
            Name = "Order",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "id", Type = new CanonicalType { Name = "ID", Kind = TypeKind.Scalar } },
                new CanonicalField { Name = "total", Type = new CanonicalType { Name = "Float", Kind = TypeKind.Scalar } },
                new CanonicalField { Name = "user", Type = userType },
            ]
        };

        var auditLogType = new CanonicalType
        {
            Name = "AuditLog",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "action", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } },
            ]
        };

        var operations = new List<CanonicalOperation>
        {
            new()
            {
                Name = "users",
                GraphQLFieldName = "users",
                Description = "Get all users",
                OperationType = OperationType.Query,
                ReturnType = new CanonicalType
                {
                    Name = "User",
                    Kind = TypeKind.List,
                    IsList = true,
                    OfType = userType
                }
            },
            new()
            {
                Name = "orderById",
                GraphQLFieldName = "orderById",
                Description = "Get order by ID",
                OperationType = OperationType.Query,
                Arguments =
                [
                    new CanonicalArgument
                    {
                        Name = "id",
                        Description = "Order ID",
                        Type = new CanonicalType
                        {
                            Name = "ID",
                            Kind = TypeKind.NonNull,
                            IsNonNull = true,
                            OfType = new CanonicalType { Name = "ID", Kind = TypeKind.Scalar }
                        },
                        IsRequired = true
                    }
                ],
                ReturnType = orderType
            },
            new()
            {
                Name = "auditLogs",
                GraphQLFieldName = "auditLogs",
                Description = "Get audit logs",
                OperationType = OperationType.Query,
                ReturnType = auditLogType
            },
            new()
            {
                Name = "createUser",
                GraphQLFieldName = "createUser",
                Description = "Create a new user",
                OperationType = OperationType.Mutation,
                Arguments =
                [
                    new CanonicalArgument
                    {
                        Name = "name",
                        Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar },
                        IsRequired = true
                    }
                ],
                ReturnType = userType
            },
            new()
            {
                Name = "__schema",
                GraphQLFieldName = "__schema",
                OperationType = OperationType.Query,
                ReturnType = new CanonicalType { Name = "__Schema", Kind = TypeKind.Object }
            },
            new()
            {
                Name = "internal_debug",
                GraphQLFieldName = "internal_debug",
                OperationType = OperationType.Query,
                ReturnType = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
            }
        };

        schemaSource.GetOperationsAsync(Arg.Any<CancellationToken>()).Returns(operations);
        schemaSource.GetTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CanonicalType>
            {
                ["User"] = userType,
                ["Order"] = orderType,
                ["AuditLog"] = auditLogType,
            });

        var options = new McpOptions
        {
            MaxOutputDepth = 2,
            ExcludedTypes = ["AuditLog"],
            ExcludedFields = ["internal_*"],
            AllowMutations = false,
            NamingPolicy = ToolNamingPolicy.VerbNoun,
            ToolPrefix = "myapi"
        };

        var canonicalizer = new SchemaCanonicalizer(
            schemaSource, NullLogger<SchemaCanonicalizer>.Instance);
        var policy = new PolicyEngine(
            Options.Create(options), NullLogger<PolicyEngine>.Instance);
        var publisher = new ToolPublisher(
            policy, Options.Create(options), NullLogger<ToolPublisher>.Instance);

        // Act
        var canonResult = await canonicalizer.CanonicalizeAsync();
        var allOps = canonResult.Queries.Concat(canonResult.Mutations).ToList();
        var filteredOps = policy.Apply(allOps);
        var tools = publisher.Publish(filteredOps);

        // Assert
        // __schema is filtered by canonicalizer
        canonResult.Queries.Should().HaveCount(4); // users, orderById, auditLogs, internal_debug
        canonResult.Mutations.Should().HaveCount(1); // createUser

        // Policy filters: mutation blocked, AuditLog excluded, internal_* excluded
        filteredOps.Should().HaveCount(2); // users, orderById

        // Tools should be published with correct names
        tools.Should().HaveCount(2);
        var toolNames = tools.Select(t => t.Name).ToList();
        toolNames.Should().Contain("myapi_get_orderById");
        toolNames.Should().Contain("myapi_get_users");

        // orderById should have required arg and proper query
        var orderTool = tools.First(t => t.Name == "myapi_get_orderById");
        orderTool.GraphQLQuery.Should().Contain("$id: ID!");
        orderTool.GraphQLQuery.Should().Contain("orderById(id: $id)");
        orderTool.InputSchema.RootElement.GetProperty("required")[0].GetString().Should().Be("id");

        // Arg description should be present
        var idProp = orderTool.InputSchema.RootElement
            .GetProperty("properties").GetProperty("id");
        idProp.GetProperty("description").GetString().Should().Be("Order ID");

        // users tool should have selection set with user fields
        var usersTool = tools.First(t => t.Name == "myapi_get_users");
        usersTool.GraphQLQuery.Should().Contain("id");
        usersTool.GraphQLQuery.Should().Contain("name");
        usersTool.GraphQLQuery.Should().Contain("email");
    }

    [Fact]
    public async Task Pipeline_with_allowlist_should_only_publish_included_fields()
    {
        var schemaSource = Substitute.For<IGraphQLSchemaSource>();

        var operations = new List<CanonicalOperation>
        {
            new()
            {
                Name = "users",
                GraphQLFieldName = "users",
                OperationType = OperationType.Query,
                ReturnType = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
            },
            new()
            {
                Name = "orders",
                GraphQLFieldName = "orders",
                OperationType = OperationType.Query,
                ReturnType = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
            },
            new()
            {
                Name = "products",
                GraphQLFieldName = "products",
                OperationType = OperationType.Query,
                ReturnType = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
            }
        };

        schemaSource.GetOperationsAsync(Arg.Any<CancellationToken>()).Returns(operations);
        schemaSource.GetTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CanonicalType>());

        var options = new McpOptions
        {
            IncludedFields = ["users", "orders"]
        };

        var canonicalizer = new SchemaCanonicalizer(
            schemaSource, NullLogger<SchemaCanonicalizer>.Instance);
        var policy = new PolicyEngine(
            Options.Create(options), NullLogger<PolicyEngine>.Instance);
        var publisher = new ToolPublisher(
            policy, Options.Create(options), NullLogger<ToolPublisher>.Instance);

        var canonResult = await canonicalizer.CanonicalizeAsync();
        var filteredOps = policy.Apply(canonResult.Queries);
        var tools = publisher.Publish(filteredOps);

        tools.Should().HaveCount(2);
        tools.Select(t => t.GraphQLFieldName).Should().BeEquivalentTo(["orders", "users"]);
    }

    [Fact]
    public async Task Pipeline_with_complex_nested_types_and_depth_limit()
    {
        var schemaSource = Substitute.For<IGraphQLSchemaSource>();

        var cityType = new CanonicalType
        {
            Name = "City",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "name", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } },
                new CanonicalField { Name = "population", Type = new CanonicalType { Name = "Int", Kind = TypeKind.Scalar } }
            ]
        };

        var addressType = new CanonicalType
        {
            Name = "Address",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "street", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } },
                new CanonicalField { Name = "city", Type = cityType }
            ]
        };

        var companyType = new CanonicalType
        {
            Name = "Company",
            Kind = TypeKind.Object,
            Fields =
            [
                new CanonicalField { Name = "name", Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar } },
                new CanonicalField { Name = "address", Type = addressType }
            ]
        };

        var operations = new List<CanonicalOperation>
        {
            new()
            {
                Name = "company",
                GraphQLFieldName = "company",
                OperationType = OperationType.Query,
                ReturnType = companyType
            }
        };

        schemaSource.GetOperationsAsync(Arg.Any<CancellationToken>()).Returns(operations);
        schemaSource.GetTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CanonicalType>());

        // Depth 2: should get Company.name, Company.address.street but NOT Company.address.city.*
        var options = new McpOptions { MaxOutputDepth = 2 };

        var canonicalizer = new SchemaCanonicalizer(
            schemaSource, NullLogger<SchemaCanonicalizer>.Instance);
        var policy = new PolicyEngine(
            Options.Create(options), NullLogger<PolicyEngine>.Instance);
        var publisher = new ToolPublisher(
            policy, Options.Create(options), NullLogger<ToolPublisher>.Instance);

        var canonResult = await canonicalizer.CanonicalizeAsync();
        var filteredOps = policy.Apply(canonResult.Queries);
        var tools = publisher.Publish(filteredOps);

        tools.Should().HaveCount(1);
        var query = tools[0].GraphQLQuery;

        query.Should().Contain("name"); // Company.name (depth 1)
        query.Should().Contain("address"); // Company.address (depth 1)
        query.Should().Contain("street"); // Company.address.street (depth 2)
        query.Should().NotContain("population"); // City.population at depth 3 — truncated
    }
}
