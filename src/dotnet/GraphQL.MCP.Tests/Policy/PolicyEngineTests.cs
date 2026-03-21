using FluentAssertions;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using GraphQL.MCP.Core.Policy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GraphQL.MCP.Tests.Policy;

public class PolicyEngineTests
{
    private static PolicyEngine CreateSut(McpOptions? options = null)
    {
        var opts = options ?? new McpOptions();
        return new PolicyEngine(
            Options.Create(opts),
            NullLogger<PolicyEngine>.Instance);
    }

    private static CanonicalOperation CreateOperation(
        string name,
        OperationType type = OperationType.Query,
        string returnTypeName = "String") => new()
        {
            Name = name,
            GraphQLFieldName = name,
            OperationType = type,
            ReturnType = new CanonicalType { Name = returnTypeName, Kind = TypeKind.Scalar }
        };

    // --- Exclusion tests ---

    [Fact]
    public void Should_exclude_mutations_by_default()
    {
        var sut = CreateSut();
        var op = CreateOperation("createUser", OperationType.Mutation);

        sut.ShouldIncludeOperation(op).Should().BeFalse();
    }

    [Fact]
    public void Should_include_mutations_when_allowed()
    {
        var sut = CreateSut(new McpOptions { AllowMutations = true });
        var op = CreateOperation("createUser", OperationType.Mutation);

        sut.ShouldIncludeOperation(op).Should().BeTrue();
    }

    [Fact]
    public void Should_exclude_introspection_fields()
    {
        var sut = CreateSut();
        var op = CreateOperation("__schema");

        sut.ShouldIncludeOperation(op).Should().BeFalse();
    }

    [Fact]
    public void Should_exclude_fields_in_ExcludedFields()
    {
        var sut = CreateSut(new McpOptions { ExcludedFields = ["password", "ssn"] });

        sut.ShouldIncludeOperation(CreateOperation("password")).Should().BeFalse();
        sut.ShouldIncludeOperation(CreateOperation("ssn")).Should().BeFalse();
        sut.ShouldIncludeOperation(CreateOperation("users")).Should().BeTrue();
    }

    [Fact]
    public void Should_exclude_operations_with_excluded_return_type()
    {
        var sut = CreateSut(new McpOptions { ExcludedTypes = ["AuditLog"] });
        var op = CreateOperation("auditLogs", returnTypeName: "AuditLog");

        sut.ShouldIncludeOperation(op).Should().BeFalse();
    }

    [Fact]
    public void Should_exclude_operations_with_excluded_argument_type()
    {
        var sut = CreateSut(new McpOptions { ExcludedTypes = ["SensitiveInput"] });
        var op = new CanonicalOperation
        {
            Name = "search",
            GraphQLFieldName = "search",
            OperationType = OperationType.Query,
            ReturnType = new CanonicalType { Name = "String", Kind = TypeKind.Scalar },
            Arguments =
            [
                new CanonicalArgument
                {
                    Name = "input",
                    Type = new CanonicalType { Name = "SensitiveInput", Kind = TypeKind.InputObject }
                }
            ]
        };

        sut.ShouldIncludeOperation(op).Should().BeFalse();
    }

    // --- Naming tests ---

    [Fact]
    public void VerbNoun_should_prefix_queries_with_get()
    {
        var sut = CreateSut();
        var op = CreateOperation("users");

        sut.TransformToolName(op).Should().Be("get_users");
    }

    [Fact]
    public void VerbNoun_should_not_prefix_mutations()
    {
        var sut = CreateSut(new McpOptions { AllowMutations = true });
        var op = CreateOperation("createUser", OperationType.Mutation);

        sut.TransformToolName(op).Should().Be("createUser");
    }

    [Fact]
    public void Should_apply_tool_prefix()
    {
        var sut = CreateSut(new McpOptions { ToolPrefix = "myapi" });
        var op = CreateOperation("users");

        sut.TransformToolName(op).Should().Be("myapi_get_users");
    }

    [Fact]
    public void Raw_naming_should_use_field_name_directly()
    {
        var sut = CreateSut(new McpOptions { NamingPolicy = ToolNamingPolicy.Raw });
        var op = CreateOperation("users");

        sut.TransformToolName(op).Should().Be("users");
    }

    [Fact]
    public void Should_sanitize_special_characters()
    {
        var sut = CreateSut(new McpOptions { NamingPolicy = ToolNamingPolicy.Raw });
        var op = CreateOperation("my-field.name");

        sut.TransformToolName(op).Should().Be("my_field_name");
    }

    // --- Apply (combined filter + limit) ---

    [Fact]
    public void Apply_should_respect_MaxToolCount()
    {
        var sut = CreateSut(new McpOptions { MaxToolCount = 2 });
        var ops = Enumerable.Range(1, 5)
            .Select(i => CreateOperation($"field{i}"))
            .ToList();

        var result = sut.Apply(ops);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Apply_should_sort_alphabetically()
    {
        var sut = CreateSut();
        var ops = new[]
        {
            CreateOperation("zebra"),
            CreateOperation("alpha"),
            CreateOperation("middle"),
        };

        var result = sut.Apply(ops);
        var names = result.Select(o => sut.TransformToolName(o)).ToList();

        names.Should().BeInAscendingOrder();
    }
}
