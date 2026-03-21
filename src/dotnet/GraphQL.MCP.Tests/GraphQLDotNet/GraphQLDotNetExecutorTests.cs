using System.Text.Json;
using FluentAssertions;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.GraphQLDotNet;
using GraphQL.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GraphQL.MCP.Tests.GraphQLDotNet;

public class GraphQLDotNetExecutorTests
{
    [Fact]
    public async Task Should_execute_simple_query()
    {
        var (executor, _) = CreateExecutor();

        var result = await executor.ExecuteAsync(new GraphQLExecutionRequest
        {
            Query = "{ hello }"
        });

        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().BeNullOrEmpty();
        result.Data.Should().NotBeNull();

        var json = JsonSerializer.Serialize(result.Data);
        json.Should().Contain("Hello, World!");
    }

    [Fact]
    public async Task Should_execute_query_with_variables()
    {
        var (executor, _) = CreateExecutor();

        var result = await executor.ExecuteAsync(new GraphQLExecutionRequest
        {
            Query = "query($name: String!) { hello(name: $name) }",
            Variables = new Dictionary<string, object?> { ["name"] = "Claude" }
        });

        result.IsSuccess.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Data);
        json.Should().Contain("Hello, Claude!");
    }

    [Fact]
    public async Task Should_execute_query_returning_list()
    {
        var (executor, _) = CreateExecutor();

        var result = await executor.ExecuteAsync(new GraphQLExecutionRequest
        {
            Query = "{ books { title author { name } } }"
        });

        result.IsSuccess.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Data);
        json.Should().Contain("The GraphQL Guide");
        json.Should().Contain("John Resig");
    }

    [Fact]
    public async Task Should_execute_query_with_argument()
    {
        var (executor, _) = CreateExecutor();

        var result = await executor.ExecuteAsync(new GraphQLExecutionRequest
        {
            Query = "query($title: String!) { bookByTitle(title: $title) { title author { name email } } }",
            Variables = new Dictionary<string, object?> { ["title"] = "GraphQL" }
        });

        result.IsSuccess.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Data);
        json.Should().Contain("The GraphQL Guide");
        json.Should().Contain("john@example.com");
    }

    [Fact]
    public async Task Should_return_errors_for_invalid_query()
    {
        var (executor, _) = CreateExecutor();

        var result = await executor.ExecuteAsync(new GraphQLExecutionRequest
        {
            Query = "{ nonExistentField }"
        });

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Should_handle_null_result()
    {
        var (executor, _) = CreateExecutor();

        var result = await executor.ExecuteAsync(new GraphQLExecutionRequest
        {
            Query = "query($title: String!) { bookByTitle(title: $title) { title } }",
            Variables = new Dictionary<string, object?> { ["title"] = "NonExistent" }
        });

        result.IsSuccess.Should().BeTrue();
        var json = JsonSerializer.Serialize(result.Data);
        json.Should().Contain("null");
    }

    [Fact]
    public async Task Should_forward_headers_via_user_context()
    {
        var (executor, _) = CreateExecutor();

        // Headers should not cause an error — they're set on UserContext
        var result = await executor.ExecuteAsync(new GraphQLExecutionRequest
        {
            Query = "{ hello }",
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer test-token" }
        });

        result.IsSuccess.Should().BeTrue();
    }

    private static (GraphQLDotNetExecutor executor, ISchema schema) CreateExecutor()
    {
        var services = new ServiceCollection();
        services.AddGraphQL(b => b
            .AddSchema<TestSchema>()
            .AddSystemTextJson()
            .AddGraphTypes(typeof(TestSchema).Assembly));

        var provider = services.BuildServiceProvider();
        var schema = provider.GetRequiredService<ISchema>();
        var documentExecuter = provider.GetRequiredService<IDocumentExecuter>();

        var executor = new GraphQLDotNetExecutor(
            documentExecuter,
            schema,
            NullLogger<GraphQLDotNetExecutor>.Instance);

        return (executor, schema);
    }
}

public class TestSchema : Schema
{
    public TestSchema(IServiceProvider provider) : base(provider)
    {
        Query = provider.GetRequiredService<TestQuery>();
    }
}

public class TestQuery : ObjectGraphType
{
    public TestQuery()
    {
        Name = "Query";

        Field<StringGraphType>("hello")
            .Argument<StringGraphType>("name", arg => arg.DefaultValue = "World")
            .Resolve(ctx =>
            {
                var name = ctx.GetArgument<string>("name") ?? "World";
                return $"Hello, {name}!";
            });

        Field<ListGraphType<TestBookType>>("books")
            .Resolve(_ => TestBookData.GetBooks());

        Field<TestBookType>("bookByTitle")
            .Argument<NonNullGraphType<StringGraphType>>("title")
            .Resolve(ctx =>
            {
                var title = ctx.GetArgument<string>("title");
                return TestBookData.GetBooks()
                    .FirstOrDefault(b => b.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
            });
    }
}

public class TestBookType : ObjectGraphType<TestBook>
{
    public TestBookType()
    {
        Name = "Book";
        Field(x => x.Title);
        Field<TestAuthorType>("author").Resolve(ctx => ctx.Source.Author);
    }
}

public class TestAuthorType : ObjectGraphType<TestAuthor>
{
    public TestAuthorType()
    {
        Name = "Author";
        Field(x => x.Name);
        Field(x => x.Email);
    }
}

public record TestBook(string Title, TestAuthor Author);
public record TestAuthor(string Name, string Email);

public static class TestBookData
{
    public static List<TestBook> GetBooks() =>
    [
        new("The GraphQL Guide", new TestAuthor("John Resig", "john@example.com")),
        new("Learning MCP", new TestAuthor("Jane Smith", "jane@example.com")),
    ];
}
