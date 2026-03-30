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
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(client, "tools/call", null, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
    }

    [Fact]
    public async Task Tools_call_with_missing_name_should_return_error()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(client, "tools/call", "{}", sessionId);

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

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("catalog")
            .GetProperty("search").GetBoolean().Should().BeTrue();
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
    public async Task Tools_list_should_include_annotations()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(client, "tools/list", null, sessionId);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var firstTool = json.RootElement.GetProperty("result").GetProperty("tools")[0];

        firstTool.GetProperty("annotations").GetProperty("domain").GetString().Should().Be("order");
        firstTool.GetProperty("annotations").GetProperty("category").GetString().Should().Be("Order");
        firstTool.GetProperty("annotations").GetProperty("tags").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain(["query", "order"]);
    }

    [Fact]
    public async Task Tools_list_should_include_semantic_hints()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(client, "tools/list", null, sessionId);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var annotations = json.RootElement.GetProperty("result").GetProperty("tools")[0]
            .GetProperty("annotations");

        annotations.GetProperty("semanticHints").GetProperty("intent").GetString()
            .Should().Be("retrieve");
        annotations.GetProperty("semanticHints").GetProperty("keywords").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain(["query", "order"]);
    }

    [Fact]
    public async Task Catalog_list_should_group_tools_by_domain()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(client, "catalog/list", null, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var domains = json.RootElement.GetProperty("result").GetProperty("domains");

        domains.GetArrayLength().Should().Be(2);
        json.RootElement.GetProperty("result").GetProperty("toolCount").GetInt32().Should().Be(2);

        var firstDomain = domains[0];
        firstDomain.GetProperty("domain").GetString().Should().Be("order");
        firstDomain.GetProperty("categories").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain(["Order"]);
        firstDomain.GetProperty("tools")[0].GetProperty("fieldName").GetString().Should().Be("order");
        firstDomain.GetProperty("toolNames").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain(["get_order"]);
    }

    [Fact]
    public async Task Catalog_list_should_include_semantic_hints()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(client, "catalog/list", null, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var firstDomain = json.RootElement.GetProperty("result").GetProperty("domains")[0];

        firstDomain.GetProperty("semanticHints").GetProperty("intents").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain("retrieve");
        firstDomain.GetProperty("semanticHints").GetProperty("keywords").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain("order");
    }

    [Fact]
    public async Task Catalog_search_should_return_ranked_matches_and_filters()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var searchParams = JsonSerializer.Serialize(new
        {
            query = "order",
            tags = new[] { "query" },
            limit = 1
        });

        var response = await SendMcpRequest(client, "catalog/search", searchParams, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var result = json.RootElement.GetProperty("result");

        result.GetProperty("totalMatches").GetInt32().Should().Be(1);
        result.GetProperty("domainCount").GetInt32().Should().Be(1);
        result.GetProperty("matches")[0].GetProperty("name").GetString().Should().Be("get_order");
        result.GetProperty("matches")[0].GetProperty("score").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("filters").GetProperty("tags").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain("query");
    }

    [Fact]
    public async Task Capabilities_catalog_should_alias_catalog_list()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(client, "capabilities/catalog", null, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("result").GetProperty("domainCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Capabilities_search_should_alias_catalog_search()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var searchParams = JsonSerializer.Serialize(new { query = "user" });
        var response = await SendMcpRequest(client, "capabilities/search", searchParams, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("result").GetProperty("matches")[0].GetProperty("name").GetString()
            .Should().Be("get_user");
    }

    [Fact]
    public async Task Tools_call_with_unknown_tool_should_return_error_content()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var callParams = JsonSerializer.Serialize(new { name = "nonexistent_tool", arguments = new { } });
        var response = await SendMcpRequest(client, "tools/call", callParams, sessionId);

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
        var catalogCapabilities = json.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("catalog");

        serverInfo.GetProperty("name").GetString().Should().Be("graphql-mcp");
        serverInfo.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        catalogCapabilities.GetProperty("search").GetBoolean().Should().BeTrue();
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

    [Fact]
    public async Task Tools_list_without_session_should_return_400()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();

        var response = await SendMcpRequest(client, "tools/list", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Be("Missing Mcp-Session-Id header");
    }

    private static async Task<IHost> CreateTestHost()
    {
        var schemaSource = Substitute.For<IGraphQLSchemaSource>();
        schemaSource.GetOperationsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CanonicalOperation>
            {
                new()
                {
                    Name = "order",
                    GraphQLFieldName = "order",
                    OperationType = OperationType.Query,
                    ReturnType = new CanonicalType
                    {
                        Name = "Order",
                        Kind = TypeKind.Object,
                        Fields =
                        [
                            new CanonicalField
                            {
                                Name = "id",
                                Type = new CanonicalType { Name = "ID", Kind = TypeKind.Scalar }
                            }
                        ]
                    },
                    Arguments = []
                },
                new()
                {
                    Name = "user",
                    GraphQLFieldName = "user",
                    OperationType = OperationType.Query,
                    ReturnType = new CanonicalType
                    {
                        Name = "User",
                        Kind = TypeKind.Object,
                        Fields =
                        [
                            new CanonicalField
                            {
                                Name = "name",
                                Type = new CanonicalType { Name = "String", Kind = TypeKind.Scalar }
                            }
                        ]
                    },
                    Arguments = []
                }
            });
        schemaSource.GetTypesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CanonicalType>());

        var executor = Substitute.For<IGraphQLExecutor>();
        executor.ExecuteAsync(Arg.Any<GraphQLExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GraphQLExecutionResult
            {
                Data = new Dictionary<string, object?>
                {
                    ["order"] = new Dictionary<string, object?> { ["id"] = "o-1" },
                    ["user"] = new Dictionary<string, object?> { ["name"] = "Ada" }
                }
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
