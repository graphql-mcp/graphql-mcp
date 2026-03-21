using FluentAssertions;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using GraphQL.MCP.Core.Canonical;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace GraphQL.MCP.Tests.Canonical;

public class SchemaCanonicalizerTests
{
    private readonly IGraphQLSchemaSource _schemaSource = Substitute.For<IGraphQLSchemaSource>();

    private SchemaCanonicalizer CreateSut() =>
        new(_schemaSource, NullLogger<SchemaCanonicalizer>.Instance);

    [Fact]
    public async Task Should_discover_queries_and_mutations()
    {
        // Arrange
        var operations = new List<CanonicalOperation>
        {
            CreateOperation("users", OperationType.Query),
            CreateOperation("orders", OperationType.Query),
            CreateOperation("createUser", OperationType.Mutation),
        };

        _schemaSource.GetOperationsAsync(Arg.Any<CancellationToken>())
            .Returns(operations);
        _schemaSource.GetTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CanonicalType>());

        var sut = CreateSut();

        // Act
        var result = await sut.CanonicalizeAsync();

        // Assert
        result.Queries.Should().HaveCount(2);
        result.Mutations.Should().HaveCount(1);
    }

    [Fact]
    public async Task Should_skip_introspection_fields()
    {
        // Arrange
        var operations = new List<CanonicalOperation>
        {
            CreateOperation("users", OperationType.Query),
            CreateOperation("__schema", OperationType.Query),
            CreateOperation("__type", OperationType.Query),
        };

        _schemaSource.GetOperationsAsync(Arg.Any<CancellationToken>())
            .Returns(operations);
        _schemaSource.GetTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CanonicalType>());

        var sut = CreateSut();

        // Act
        var result = await sut.CanonicalizeAsync();

        // Assert
        result.Queries.Should().HaveCount(1);
        result.Queries[0].Name.Should().Be("users");
    }

    [Fact]
    public async Task Should_return_empty_when_no_operations()
    {
        _schemaSource.GetOperationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CanonicalOperation>());
        _schemaSource.GetTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CanonicalType>());

        var sut = CreateSut();
        var result = await sut.CanonicalizeAsync();

        result.Queries.Should().BeEmpty();
        result.Mutations.Should().BeEmpty();
    }

    private static CanonicalOperation CreateOperation(string name, OperationType type) => new()
    {
        Name = name,
        GraphQLFieldName = name,
        OperationType = type,
        ReturnType = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
    };
}
