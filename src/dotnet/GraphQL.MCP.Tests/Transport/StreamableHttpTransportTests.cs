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
        json.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("prompts")
            .GetProperty("listChanged").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("catalog")
            .GetProperty("search").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("resources")
            .GetProperty("read").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_should_include_authorization_metadata_capability_when_configured()
    {
        using var host = await CreateTestHost(options =>
        {
            options.Authorization.Mode = McpAuthMode.Passthrough;
            options.Authorization.RequiredScopes.Add("orders.read");
            options.Authorization.Metadata.Issuer = "https://auth.example.com";
            options.Authorization.Metadata.AuthorizationEndpoint = "https://auth.example.com/authorize";
            options.Authorization.Metadata.TokenEndpoint = "https://auth.example.com/token";
        });
        using var client = host.GetTestClient();

        var response = await SendMcpRequest(client, "initialize", null);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var authorization = json.RootElement.GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("authorization");

        authorization.GetProperty("mode").GetString().Should().Be("passthrough");
        authorization.GetProperty("requiredScopes").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain("orders.read");
        authorization.GetProperty("oauth2").GetProperty("metadata").GetBoolean().Should().BeTrue();
        authorization.GetProperty("oauth2").GetProperty("resource").GetString()
            .Should().Be("graphql-mcp://auth/metadata");
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
    public async Task Prompts_list_should_include_discovery_prompts()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(client, "prompts/list", null, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var prompts = json.RootElement.GetProperty("result").GetProperty("prompts");
        prompts.GetArrayLength().Should().Be(6);
        prompts[0].GetProperty("name").GetString().Should().Be("explore_catalog");
        prompts[1].GetProperty("arguments")[0].GetProperty("name").GetString().Should().Be("domain");
        prompts.EnumerateArray()
            .Select(prompt => prompt.GetProperty("name").GetString())
            .Should().Contain(["plan_task_workflow", "compare_tools_for_task", "prepare_tool_call"]);
    }

    [Fact]
    public async Task Prompts_get_should_embed_domain_resource()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(
            client,
            "prompts/get",
            "{\"name\":\"explore_domain\",\"arguments\":{\"domain\":\"order\"}}",
            sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("description").GetString().Should().Contain("order");
        var messages = result.GetProperty("messages");
        messages.GetArrayLength().Should().Be(2);
        messages[1].GetProperty("content").GetProperty("type").GetString().Should().Be("resource");
        messages[1].GetProperty("content").GetProperty("resource").GetProperty("uri").GetString()
            .Should().Be("graphql-mcp://catalog/domain/order");
    }

    [Fact]
    public async Task Prompts_get_prepare_tool_call_should_embed_pack_and_tool_resources()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(
            client,
            "prompts/get",
            "{\"name\":\"prepare_tool_call\",\"arguments\":{\"tool\":\"get_order\",\"task\":\"fetch an order by id\"}}",
            sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var messages = json.RootElement.GetProperty("result").GetProperty("messages");
        messages.GetArrayLength().Should().Be(3);
        messages[1].GetProperty("content").GetProperty("resource").GetProperty("uri").GetString()
            .Should().Be("graphql-mcp://packs/discovery/safe-tool-call");
        messages[2].GetProperty("content").GetProperty("resource").GetProperty("uri").GetString()
            .Should().Be("graphql-mcp://catalog/tool/get_order");
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
    public async Task Resources_list_should_include_overview_and_domain_resources()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(client, "resources/list", null, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var resources = json.RootElement.GetProperty("result").GetProperty("resources");

        resources.GetArrayLength().Should().Be(8);
        resources[0].GetProperty("uri").GetString().Should().Be("graphql-mcp://catalog/overview");
        resources.EnumerateArray()
            .Select(resource => resource.GetProperty("uri").GetString())
            .Should().Contain([
                "graphql-mcp://packs/discovery/start-here",
                "graphql-mcp://catalog/domain/order",
                "graphql-mcp://catalog/tool/get_order"
            ]);
    }

    [Fact]
    public async Task Resources_list_should_include_authorization_metadata_when_configured()
    {
        using var host = await CreateTestHost(options =>
        {
            options.Authorization.Mode = McpAuthMode.Passthrough;
            options.Authorization.RequiredScopes.Add("orders.read");
        });
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(client, "resources/list", null, sessionId);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        json.RootElement.GetProperty("result").GetProperty("resources").EnumerateArray()
            .Select(resource => resource.GetProperty("uri").GetString())
            .Should().Contain("graphql-mcp://auth/metadata");
    }

    [Fact]
    public async Task Resources_read_should_return_domain_summary()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(
            client,
            "resources/read",
            "{\"uri\":\"graphql-mcp://catalog/domain/order\"}",
            sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var content = json.RootElement.GetProperty("result").GetProperty("contents")[0];
        content.GetProperty("uri").GetString().Should().Be("graphql-mcp://catalog/domain/order");

        using var payload = JsonDocument.Parse(content.GetProperty("text").GetString()!);
        payload.RootElement.GetProperty("kind").GetString().Should().Be("domainSummary");
        payload.RootElement.GetProperty("domain").GetString().Should().Be("order");
        payload.RootElement.GetProperty("toolNames").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain("get_order");
    }

    [Fact]
    public async Task Resources_read_should_return_tool_summary()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(
            client,
            "resources/read",
            "{\"uri\":\"graphql-mcp://catalog/tool/get_order\"}",
            sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var content = json.RootElement.GetProperty("result").GetProperty("contents")[0];

        using var payload = JsonDocument.Parse(content.GetProperty("text").GetString()!);
        payload.RootElement.GetProperty("kind").GetString().Should().Be("toolSummary");
        payload.RootElement.GetProperty("name").GetString().Should().Be("get_order");
        payload.RootElement.GetProperty("requiredArguments").EnumerateArray()
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Resources_read_should_return_discovery_pack()
    {
        using var host = await CreateTestHost();
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(
            client,
            "resources/read",
            "{\"uri\":\"graphql-mcp://packs/discovery/start-here\"}",
            sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var content = json.RootElement.GetProperty("result").GetProperty("contents")[0];

        using var payload = JsonDocument.Parse(content.GetProperty("text").GetString()!);
        payload.RootElement.GetProperty("kind").GetString().Should().Be("resourcePack");
        payload.RootElement.GetProperty("pack").GetString().Should().Be("start-here");
        payload.RootElement.GetProperty("recommendedPrompts").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain("plan_task_workflow");
    }

    [Fact]
    public async Task Resources_read_should_return_authorization_metadata()
    {
        using var host = await CreateTestHost(options =>
        {
            options.Authorization.Mode = McpAuthMode.Passthrough;
            options.Authorization.RequiredScopes.Add("orders.read");
            options.Authorization.RequiredScopes.Add("orders.write");
            options.Authorization.Metadata.Issuer = "https://auth.example.com";
            options.Authorization.Metadata.AuthorizationEndpoint = "https://auth.example.com/authorize";
            options.Authorization.Metadata.TokenEndpoint = "https://auth.example.com/token";
        });
        using var client = host.GetTestClient();
        var sessionId = await InitializeSessionAsync(client);

        var response = await SendMcpRequest(
            client,
            "resources/read",
            "{\"uri\":\"graphql-mcp://auth/metadata\"}",
            sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var content = json.RootElement.GetProperty("result").GetProperty("contents")[0];
        using var payload = JsonDocument.Parse(content.GetProperty("text").GetString()!);

        payload.RootElement.GetProperty("kind").GetString().Should().Be("authorizationMetadata");
        payload.RootElement.GetProperty("mode").GetString().Should().Be("passthrough");
        payload.RootElement.GetProperty("requiredScopes").EnumerateArray()
            .Select(element => element.GetString())
            .Should().Contain(["orders.read", "orders.write"]);
        payload.RootElement.GetProperty("oauth2").GetProperty("issuer").GetString()
            .Should().Be("https://auth.example.com");
    }

    [Fact]
    public async Task Well_known_oauth_metadata_route_should_return_metadata_when_configured()
    {
        using var host = await CreateTestHost(options =>
        {
            options.Authorization.Mode = McpAuthMode.Passthrough;
            options.Authorization.RequiredScopes.Add("orders.read");
            options.Authorization.Metadata.Issuer = "https://auth.example.com";
            options.Authorization.Metadata.AuthorizationEndpoint = "https://auth.example.com/authorize";
            options.Authorization.Metadata.TokenEndpoint = "https://auth.example.com/token";
            options.Authorization.Metadata.ServiceDocumentation = "https://docs.example.com/auth";
        });
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/mcp/.well-known/oauth-authorization-server");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("issuer").GetString().Should().Be("https://auth.example.com");
        json.RootElement.GetProperty("authorization_endpoint").GetString()
            .Should().Be("https://auth.example.com/authorize");
        json.RootElement.GetProperty("x_graphql_mcp").GetProperty("resource_uri").GetString()
            .Should().Be("graphql-mcp://auth/metadata");
    }

    [Fact]
    public async Task Stdio_message_handler_should_preserve_session_across_calls()
    {
        using var host = await CreateTestHost(options => options.Transport = McpTransport.Stdio);
        var transport = host.Services.GetRequiredService<StreamableHttpTransport>();

        var initialize = await transport.HandleStdioMessageAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}",
            null,
            CancellationToken.None);

        initialize.SessionId.Should().NotBeNullOrWhiteSpace();

        var toolsList = await transport.HandleStdioMessageAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}",
            initialize.SessionId,
            CancellationToken.None);

        using var json = JsonDocument.Parse(toolsList.ResponseJson);
        json.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Stdio_hosted_service_should_emit_jsonrpc_lines()
    {
        using var host = await CreateTestHost(options => options.Transport = McpTransport.Stdio);
        var transport = host.Services.GetRequiredService<StreamableHttpTransport>();
        var logger = host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StdioMcpHostedService>>();
        var service = new StdioMcpHostedService(
            transport,
            Microsoft.Extensions.Options.Options.Create(new McpOptions { Transport = McpTransport.Stdio }),
            logger);

        var input = new StringReader(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}\n" +
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}\n");
        var output = new StringWriter();

        await service.RunAsync(input, output, CancellationToken.None);

        var lines = output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);

        using var initJson = JsonDocument.Parse(lines[0]);
        using var listJson = JsonDocument.Parse(lines[1]);
        initJson.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString()
            .Should().Be("2025-06-18");
        listJson.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength()
            .Should().Be(2);
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
        var resourceCapabilities = json.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("resources");

        serverInfo.GetProperty("name").GetString().Should().Be("graphql-mcp");
        serverInfo.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        catalogCapabilities.GetProperty("search").GetBoolean().Should().BeTrue();
        resourceCapabilities.GetProperty("listChanged").GetBoolean().Should().BeTrue();
        resourceCapabilities.GetProperty("read").GetBoolean().Should().BeTrue();
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

    private static async Task<IHost> CreateTestHost(Action<McpOptions>? configure = null)
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
                    services.AddGraphQLMcp(configure);
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
