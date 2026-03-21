using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Canonical;
using GraphQL.MCP.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace GraphQL.MCP.Tests.Transport;

public class StreamableHttpTransportTests
{
    [Fact]
    public async Task Should_return_405_for_GET_requests()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/mcp");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Ping_should_return_empty_result()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var response = await SendMcpRequest(client, "ping", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
    }

    [Fact]
    public async Task Missing_method_should_return_invalid_request()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var body = "{\"jsonrpc\":\"2.0\",\"id\":1}";
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/mcp", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32600);
    }

    [Fact]
    public async Task Tools_call_with_missing_params_should_return_error()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var response = await SendMcpRequest(client, "tools/call", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
    }

    [Fact]
    public async Task Tools_call_with_missing_name_should_return_error()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var response = await SendMcpRequest(client, "tools/call", "{}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
    }

    [Fact]
    public async Task Session_id_should_be_returned_on_initialize()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var response = await SendMcpRequest(client, "initialize", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Mcp-Session-Id").Should().BeTrue();
        response.Headers.GetValues("Mcp-Session-Id").First().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Valid_session_id_should_be_accepted()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        // Initialize to get session ID
        var initResponse = await SendMcpRequest(client, "initialize", null);
        var sessionId = initResponse.Headers.GetValues("Mcp-Session-Id").First();

        // Use session ID for subsequent request
        var body = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Mcp-Session-Id", sessionId);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength().Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task Tools_call_with_unknown_tool_should_return_error_content()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var callParams = JsonSerializer.Serialize(new { name = "nonexistent_tool", arguments = new { } });
        var response = await SendMcpRequest(client, "tools/call", callParams);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("content")[0].GetProperty("text").GetString()
            .Should().Contain("not found");
    }

    [Fact]
    public async Task Initialize_should_include_server_version()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var response = await SendMcpRequest(client, "initialize", null);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var serverInfo = json.RootElement.GetProperty("result").GetProperty("serverInfo");

        serverInfo.GetProperty("name").GetString().Should().Be("graphql-mcp");
        serverInfo.GetProperty("version").GetString().Should().Be("0.1.0");
    }

    [Fact]
    public async Task JsonRpc_response_should_include_id()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var body = "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"ping\"}";
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/mcp", content);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("id").GetInt64().Should().Be(42);
    }

    private static async Task<IHost> CreateTestHost()
    {
        var schemaSource = Substitute.For<IGraphQLSchemaSource>();
        schemaSource.GetOperationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CanonicalOperation>
            {
                new()
                {
                    Name = "hello",
                    GraphQLFieldName = "hello",
                    OperationType = OperationType.Query,
                    ReturnType = new CanonicalType { Name = "String", Kind = TypeKind.Scalar },
                    Arguments = []
                }
            });
        schemaSource.GetTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CanonicalType>());

        var executor = Substitute.For<IGraphQLExecutor>();
        executor.ExecuteAsync(Arg.Any<GraphQLExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GraphQLExecutionResult
            {
                Data = new Dictionary<string, object?> { ["hello"] = "world" }
            });

        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton(schemaSource);
                    services.AddSingleton(executor);
                    services.AddGraphQLMcp();
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGraphQLMcp();
                    });
                });
            })
            .StartAsync();
    }

    private static async Task<HttpResponseMessage> SendMcpRequest(
        HttpClient client, string method, string? paramsJson)
    {
        var body = paramsJson is not null
            ? $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"{method}\",\"params\":{paramsJson}}}"
            : $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"{method}\"}}";

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.PostAsync("/mcp", content);
    }
}
