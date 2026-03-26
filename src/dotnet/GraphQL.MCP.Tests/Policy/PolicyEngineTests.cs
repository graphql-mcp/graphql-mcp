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
    public void Should_exclude_operations_without_description_when_required()
    {
        var sut = CreateSut(new McpOptions { RequireDescriptionsForPublishedTools = true });
        var op = CreateOperation("users");

        sut.ShouldIncludeOperation(op).Should().BeFalse();
    }

    [Fact]
    public void Should_exclude_operations_exceeding_MaxArgumentCount()
    {
        var sut = CreateSut(new McpOptions { MaxArgumentCount = 1 });
        var op = new CanonicalOperation
        {
            Name = "searchUsers",
            GraphQLFieldName = "searchUsers",
            OperationType = OperationType.Query,
            ReturnType = new CanonicalType { Name = "String", Kind = TypeKind.Scalar },
            Arguments =
            [
                new CanonicalArgument
                {
                    Name = "name",
                    Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
                },
                new CanonicalArgument
                {
                    Name = "email",
                    Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
                }
            ]
        };

        sut.ShouldIncludeOperation(op).Should().BeFalse();
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

    // --- Glob pattern tests ---

    [Fact]
    public void Should_exclude_fields_matching_glob_pattern_with_wildcard()
    {
        var sut = CreateSut(new McpOptions { ExcludedFields = ["*password*", "*Password*"] });

        sut.ShouldIncludeOperation(CreateOperation("userPassword")).Should().BeFalse();
        sut.ShouldIncludeOperation(CreateOperation("passwordReset")).Should().BeFalse();
        sut.ShouldIncludeOperation(CreateOperation("password")).Should().BeFalse();
        sut.ShouldIncludeOperation(CreateOperation("users")).Should().BeTrue();
    }

    [Fact]
    public void Should_exclude_fields_matching_prefix_glob()
    {
        var sut = CreateSut(new McpOptions { ExcludedFields = ["internal_*"] });

        sut.ShouldIncludeOperation(CreateOperation("internal_debug")).Should().BeFalse();
        sut.ShouldIncludeOperation(CreateOperation("internal_metrics")).Should().BeFalse();
        sut.ShouldIncludeOperation(CreateOperation("users")).Should().BeTrue();
    }

    [Fact]
    public void Should_exclude_fields_matching_suffix_glob()
    {
        var sut = CreateSut(new McpOptions { ExcludedFields = ["*_secret"] });

        sut.ShouldIncludeOperation(CreateOperation("api_secret")).Should().BeFalse();
        sut.ShouldIncludeOperation(CreateOperation("users")).Should().BeTrue();
    }

    [Fact]
    public void Should_exclude_fields_matching_question_mark_glob()
    {
        var sut = CreateSut(new McpOptions { ExcludedFields = ["field?"] });

        sut.ShouldIncludeOperation(CreateOperation("fieldA")).Should().BeFalse();
        sut.ShouldIncludeOperation(CreateOperation("field1")).Should().BeFalse();
        sut.ShouldIncludeOperation(CreateOperation("fields")).Should().BeFalse();
        sut.ShouldIncludeOperation(CreateOperation("field")).Should().BeTrue("'field' is 5 chars, pattern 'field?' requires exactly 6");
        sut.ShouldIncludeOperation(CreateOperation("field12")).Should().BeTrue("two chars should not match single '?'");
    }

    // --- Allowlist tests ---

    [Fact]
    public void Should_only_include_fields_in_IncludedFields()
    {
        var sut = CreateSut(new McpOptions { IncludedFields = ["users", "orders"] });

        sut.ShouldIncludeOperation(CreateOperation("users")).Should().BeTrue();
        sut.ShouldIncludeOperation(CreateOperation("orders")).Should().BeTrue();
        sut.ShouldIncludeOperation(CreateOperation("products")).Should().BeFalse();
    }

    [Fact]
    public void Should_support_glob_patterns_in_IncludedFields()
    {
        var sut = CreateSut(new McpOptions { IncludedFields = ["get*", "list*"] });

        sut.ShouldIncludeOperation(CreateOperation("getUsers")).Should().BeTrue();
        sut.ShouldIncludeOperation(CreateOperation("listOrders")).Should().BeTrue();
        sut.ShouldIncludeOperation(CreateOperation("createUser")).Should().BeFalse();
    }

    // --- Return type unwrapping tests ---

    [Fact]
    public void Should_exclude_wrapped_NonNull_return_type()
    {
        var sut = CreateSut(new McpOptions { ExcludedTypes = ["AuditLog"] });
        var op = new CanonicalOperation
        {
            Name = "auditLogs",
            GraphQLFieldName = "auditLogs",
            OperationType = OperationType.Query,
            ReturnType = new CanonicalType
            {
                Name = "AuditLog",
                Kind = TypeKind.NonNull,
                IsNonNull = true,
                OfType = new CanonicalType { Name = "AuditLog", Kind = TypeKind.Object }
            }
        };

        sut.ShouldIncludeOperation(op).Should().BeFalse();
    }

    [Fact]
    public void Should_exclude_wrapped_List_return_type()
    {
        var sut = CreateSut(new McpOptions { ExcludedTypes = ["AuditLog"] });
        var op = new CanonicalOperation
        {
            Name = "auditLogs",
            GraphQLFieldName = "auditLogs",
            OperationType = OperationType.Query,
            ReturnType = new CanonicalType
            {
                Name = "List",
                Kind = TypeKind.List,
                IsList = true,
                OfType = new CanonicalType { Name = "AuditLog", Kind = TypeKind.Object }
            }
        };

        sut.ShouldIncludeOperation(op).Should().BeFalse();
    }

    // --- Long name tests ---

    [Fact]
    public void Should_truncate_tool_names_exceeding_64_chars()
    {
        var sut = CreateSut(new McpOptions { NamingPolicy = ToolNamingPolicy.Raw });
        var longName = new string('a', 70);
        var op = CreateOperation(longName);

        var name = sut.TransformToolName(op);

        name.Length.Should().BeLessOrEqualTo(64);
    }

    [Fact]
    public void Should_use_deterministic_suffix_for_truncated_tool_names()
    {
        var longName = new string('a', 70);

        var first = CreateSut(new McpOptions { NamingPolicy = ToolNamingPolicy.Raw })
            .TransformToolName(CreateOperation(longName));
        var second = CreateSut(new McpOptions { NamingPolicy = ToolNamingPolicy.Raw })
            .TransformToolName(CreateOperation(longName));

        first.Should().Be(second);
        first.Should().MatchRegex("^a{55}_[a-f0-9]{8}$");
    }

    // --- MaxToolCount boundary ---

    [Fact]
    public void Apply_should_return_all_when_exactly_at_MaxToolCount()
    {
        var sut = CreateSut(new McpOptions { MaxToolCount = 3 });
        var ops = Enumerable.Range(1, 3)
            .Select(i => CreateOperation($"field{i}"))
            .ToList();

        var result = sut.Apply(ops);

        result.Should().HaveCount(3);
    }

    // --- IsFieldExcluded tests ---

    [Fact]
    public void IsFieldExcluded_should_return_true_for_excluded_fields()
    {
        var sut = CreateSut(new McpOptions { ExcludedFields = ["internalNotes", "password"] });

        sut.IsFieldExcluded("internalNotes").Should().BeTrue();
        sut.IsFieldExcluded("password").Should().BeTrue();
        sut.IsFieldExcluded("title").Should().BeFalse();
    }

    [Fact]
    public void IsFieldExcluded_should_support_glob_patterns()
    {
        var sut = CreateSut(new McpOptions { ExcludedFields = ["internal*", "*secret*"] });

        sut.IsFieldExcluded("internalNotes").Should().BeTrue();
        sut.IsFieldExcluded("internalScore").Should().BeTrue();
        sut.IsFieldExcluded("apisecretkey").Should().BeTrue("lowercase 'secret' matches glob pattern");
        sut.IsFieldExcluded("apiSecretKey").Should().BeFalse("glob matching is case-sensitive; 'Secret' != 'secret'");
        sut.IsFieldExcluded("title").Should().BeFalse();
    }

    [Fact]
    public void IsFieldExcluded_should_return_false_when_no_exclusions()
    {
        var sut = CreateSut(new McpOptions());

        sut.IsFieldExcluded("anything").Should().BeFalse();
    }
}
