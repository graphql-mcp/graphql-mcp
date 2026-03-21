using System.Text.Json;
using FluentAssertions;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using GraphQL.MCP.Core.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace GraphQL.MCP.Tests.Execution;

public class ToolExecutorTests
{
    private readonly IGraphQLExecutor _graphqlExecutor = Substitute.For<IGraphQLExecutor>();

    private ToolExecutor CreateSut()
    {
        var executor = new ToolExecutor(_graphqlExecutor, NullLogger<ToolExecutor>.Instance);
        return executor;
    }

    private static McpToolDescriptor CreateTool(string name, string query = "query { test }") =>
        new()
        {
            Name = name,
            Description = "Test tool",
            InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}"""),
            GraphQLQuery = query,
            OperationType = OperationType.Query,
            GraphQLFieldName = name,
            ArgumentMapping = new Dictionary<string, string>()
        };

    [Fact]
    public async Task Should_return_error_for_unknown_tool()
    {
        var sut = CreateSut();

        var result = await sut.ExecuteAsync("nonexistent", null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task Should_execute_registered_tool()
    {
        var sut = CreateSut();
        sut.RegisterTools([CreateTool("test")]);

        _graphqlExecutor.ExecuteAsync(Arg.Any<GraphQLExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GraphQLExecutionResult
            {
                Data = new Dictionary<string, object?> { ["test"] = "hello" }
            });

        var result = await sut.ExecuteAsync("test", null);

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Should_pass_arguments_as_variables()
    {
        var sut = CreateSut();
        var tool = new McpToolDescriptor
        {
            Name = "getUserById",
            Description = "Get user",
            InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"id":{"type":"string"}}}"""),
            GraphQLQuery = "query ($id: ID!) { userById(id: $id) { name } }",
            OperationType = OperationType.Query,
            GraphQLFieldName = "userById",
            ArgumentMapping = new Dictionary<string, string> { ["id"] = "id" }
        };
        sut.RegisterTools([tool]);

        GraphQLExecutionRequest? capturedRequest = null;
        _graphqlExecutor.ExecuteAsync(Arg.Any<GraphQLExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRequest = callInfo.ArgAt<GraphQLExecutionRequest>(0);
                return new GraphQLExecutionResult
                {
                    Data = new Dictionary<string, object?> { ["userById"] = new Dictionary<string, object?> { ["name"] = "Alice" } }
                };
            });

        var args = JsonDocument.Parse("""{"id": "123"}""").RootElement;
        await sut.ExecuteAsync("getUserById", args);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Variables.Should().ContainKey("id");
        capturedRequest.Variables!["id"].Should().Be("123");
    }

    [Fact]
    public async Task Should_return_error_on_graphql_errors()
    {
        var sut = CreateSut();
        sut.RegisterTools([CreateTool("failing")]);

        _graphqlExecutor.ExecuteAsync(Arg.Any<GraphQLExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GraphQLExecutionResult
            {
                Errors = [new GraphQLError { Message = "Field not found" }]
            });

        var result = await sut.ExecuteAsync("failing", null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Field not found");
    }

    [Fact]
    public async Task Should_pass_headers_when_provided()
    {
        var sut = CreateSut();
        sut.RegisterTools([CreateTool("authed")]);

        GraphQLExecutionRequest? capturedRequest = null;
        _graphqlExecutor.ExecuteAsync(Arg.Any<GraphQLExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRequest = callInfo.ArgAt<GraphQLExecutionRequest>(0);
                return new GraphQLExecutionResult { Data = new Dictionary<string, object?> { ["authed"] = "ok" } };
            });

        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer token123" };
        await sut.ExecuteAsync("authed", null, headers);

        capturedRequest!.Headers.Should().ContainKey("Authorization");
        capturedRequest.Headers!["Authorization"].Should().Be("Bearer token123");
    }

    [Fact]
    public void HasTool_should_return_true_for_registered_tools()
    {
        var sut = CreateSut();
        sut.RegisterTools([CreateTool("myTool")]);

        sut.HasTool("myTool").Should().BeTrue();
        sut.HasTool("unknown").Should().BeFalse();
    }

    [Fact]
    public async Task Should_handle_exceptions_gracefully()
    {
        var sut = CreateSut();
        sut.RegisterTools([CreateTool("broken")]);

        _graphqlExecutor.ExecuteAsync(Arg.Any<GraphQLExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns<GraphQLExecutionResult>(_ => throw new InvalidOperationException("boom"));

        var result = await sut.ExecuteAsync("broken", null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("internal error");
    }

    [Fact]
    public void RegisterTools_should_throw_for_duplicate_tool_names()
    {
        var sut = CreateSut();

        var act = () => sut.RegisterTools(
        [
            CreateTool("duplicate", "query { first }"),
            CreateTool("duplicate", "query { second }")
        ]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate MCP tool name*");
    }
}
