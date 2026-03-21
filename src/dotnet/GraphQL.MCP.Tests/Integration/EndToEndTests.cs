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

namespace GraphQL.MCP.Tests.Integration;

public class EndToEndTests
{
    [Fact]
    public async Task Initialize_should_return_capabilities()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var response = await SendMcpRequest(client, "initialize", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Session ID should be returned
        response.Headers.Contains("Mcp-Session-Id").Should().BeTrue();

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var result = json.RootElement.GetProperty("result");

        result.GetProperty("protocolVersion").GetString().Should().Be("2025-06-18");
        result.GetProperty("capabilities").GetProperty("tools").GetProperty("listChanged").GetBoolean().Should().BeTrue();
        result.GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("graphql-mcp");
    }

    [Fact]
    public async Task Tools_list_should_return_discovered_tools()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(client, "tools/list", null, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var tools = json.RootElement.GetProperty("result").GetProperty("tools");

        tools.GetArrayLength().Should().BeGreaterThan(0);
        tools[0].GetProperty("name").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Tools_call_should_execute_and_return_result()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        // First get tools list
        var listResponse = await SendMcpRequest(client, "tools/list", null, sessionId);
        var listJson = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync());
        var toolName = listJson.RootElement.GetProperty("result")
            .GetProperty("tools")[0]
            .GetProperty("name").GetString()!;

        // Call the tool
        var callParams = JsonSerializer.Serialize(new { name = toolName, arguments = new { } });
        var response = await SendMcpRequest(client, "tools/call", callParams, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("content").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Unknown_method_should_return_error()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var response = await SendMcpRequest(client, "unknown/method", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);
    }

    [Fact]
    public async Task Invalid_json_should_return_400()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var content = new StringContent("not json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/mcp", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_session_id_should_return_404()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        // Send tools/list with a fabricated session ID (not from initialize)
        var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Mcp-Session-Id", "nonexistent-session-id");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<IHost> CreateTestHost()
    {
        // Create mock schema source and executor
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

        var host = await new HostBuilder()
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

        // Tools are initialized by McpToolInitializationService during StartAsync
        return host;
    }

    private static async Task<HttpResponseMessage> SendMcpRequest(
        HttpClient client, string method, string? paramsJson, string? sessionId = null)
    {
        var body = paramsJson is not null
            ? $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"{method}\",\"params\":{paramsJson}}}"
            : $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"{method}\"}}";

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> InitializeSessionAsync(HttpClient client)
    {
        var response = await SendMcpRequest(client, "initialize", null);
        return response.Headers.GetValues("Mcp-Session-Id").First();
    }
}
