using System.Text.Json;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Core.Execution;
using GraphQL.MCP.Core.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GraphQL.MCP.AspNetCore;

/// <summary>
/// Handles Streamable HTTP transport for MCP protocol messages.
/// Implements the MCP 2025-06-18 Streamable HTTP transport specification.
/// </summary>
public sealed class StreamableHttpTransport
{
    private readonly ToolExecutor _toolExecutor;
    private readonly IReadOnlyList<McpToolDescriptor> _tools;
    private readonly McpOptions _options;
    private readonly ILogger<StreamableHttpTransport> _logger;

    public StreamableHttpTransport(
        ToolExecutor toolExecutor,
        IReadOnlyList<McpToolDescriptor> tools,
        IOptions<McpOptions> options,
        ILogger<StreamableHttpTransport> logger)
    {
        _toolExecutor = toolExecutor;
        _tools = tools;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Handles an incoming MCP HTTP request.
    /// </summary>
    public async Task HandleRequestAsync(HttpContext context)
    {
        if (context.Request.Method != HttpMethods.Post)
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        JsonDocument? doc = null;
        try
        {
            doc = await JsonDocument.ParseAsync(
                context.Request.Body,
                cancellationToken: context.RequestAborted);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteJsonRpcError(context, null, -32700, "Parse error");
            return;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("method", out var methodElement))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await WriteJsonRpcError(context, GetId(doc.RootElement), -32600, "Invalid request");
                return;
            }

            var method = methodElement.GetString();
            var id = GetId(doc.RootElement);

            _logger.LogDebug("MCP request: method={Method}, id={Id}", method, id);

            switch (method)
            {
                case "initialize":
                    await HandleInitialize(context, id);
                    break;
                case "tools/list":
                    await HandleToolsList(context, id);
                    break;
                case "tools/call":
                    await HandleToolsCall(context, doc.RootElement, id);
                    break;
                case "ping":
                    await WriteJsonRpcResult(context, id, "{}");
                    break;
                default:
                    await WriteJsonRpcError(context, id, -32601, $"Method not found: {method}");
                    break;
            }
        }
    }

    private async Task HandleInitialize(HttpContext context, object? id)
    {
        var result = new
        {
            protocolVersion = "2025-06-18",
            capabilities = new
            {
                tools = new { listChanged = true }
            },
            serverInfo = new
            {
                name = "graphql-mcp",
                version = "0.1.0"
            }
        };

        await WriteJsonRpcResult(context, id, JsonSerializer.Serialize(result, JsonOptions));
    }

    private async Task HandleToolsList(HttpContext context, object? id)
    {
        var tools = _tools.Select(t => new
        {
            name = t.Name,
            description = t.Description,
            inputSchema = JsonSerializer.Deserialize<object>(
                t.InputSchema.RootElement.GetRawText(), JsonOptions)
        });

        var result = new { tools };
        await WriteJsonRpcResult(context, id, JsonSerializer.Serialize(result, JsonOptions));
    }

    private async Task HandleToolsCall(HttpContext context, JsonElement root, object? id)
    {
        if (!root.TryGetProperty("params", out var paramsElement))
        {
            await WriteJsonRpcError(context, id, -32602, "Missing params");
            return;
        }

        if (!paramsElement.TryGetProperty("name", out var nameElement))
        {
            await WriteJsonRpcError(context, id, -32602, "Missing tool name");
            return;
        }

        var toolName = nameElement.GetString()!;
        JsonElement? arguments = paramsElement.TryGetProperty("arguments", out var args) ? args : null;

        McpActivitySource.ToolInvocations.Add(1, new KeyValuePair<string, object?>("tool", toolName));

        // Extract auth headers for passthrough
        Dictionary<string, string>? headers = null;
        if (_options.Authorization.Mode == McpAuthMode.Passthrough &&
            context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            headers = new Dictionary<string, string>
            {
                ["Authorization"] = authHeader.ToString()
            };
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var executionResult = await _toolExecutor.ExecuteAsync(
            toolName, arguments, headers, context.RequestAborted);
        sw.Stop();

        McpActivitySource.ToolDuration.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("tool", toolName));

        if (executionResult.IsSuccess)
        {
            var result = new
            {
                content = new[]
                {
                    new { type = "text", text = executionResult.Content }
                }
            };
            await WriteJsonRpcResult(context, id, JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            McpActivitySource.ToolErrors.Add(1, new KeyValuePair<string, object?>("tool", toolName));

            var result = new
            {
                content = new[]
                {
                    new { type = "text", text = executionResult.ErrorMessage }
                },
                isError = true
            };
            await WriteJsonRpcResult(context, id, JsonSerializer.Serialize(result, JsonOptions));
        }
    }

    private static object? GetId(JsonElement root)
    {
        if (root.TryGetProperty("id", out var idElement))
        {
            return idElement.ValueKind switch
            {
                JsonValueKind.Number => idElement.GetInt64(),
                JsonValueKind.String => idElement.GetString(),
                _ => null
            };
        }
        return null;
    }

    private static async Task WriteJsonRpcResult(HttpContext context, object? id, string result)
    {
        context.Response.ContentType = "application/json";
        var response = $"{{\"jsonrpc\":\"2.0\",\"id\":{JsonSerializer.Serialize(id)},\"result\":{result}}}";
        await context.Response.WriteAsync(response);
    }

    private static async Task WriteJsonRpcError(HttpContext context, object? id, int code, string message)
    {
        context.Response.ContentType = "application/json";
        var response = $"{{\"jsonrpc\":\"2.0\",\"id\":{JsonSerializer.Serialize(id)},\"error\":{{\"code\":{code},\"message\":{JsonSerializer.Serialize(message)}}}}}";
        await context.Response.WriteAsync(response);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
