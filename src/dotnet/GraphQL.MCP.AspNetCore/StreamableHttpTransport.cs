using System.Collections.Concurrent;
using System.Text.Json;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Core.Execution;
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
    private readonly McpToolRegistry _toolRegistry;
    private readonly McpOptions _options;
    private readonly ILogger<StreamableHttpTransport> _logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new();

    public StreamableHttpTransport(
        ToolExecutor toolExecutor,
        McpToolRegistry toolRegistry,
        IOptions<McpOptions> options,
        ILogger<StreamableHttpTransport> logger)
    {
        _toolExecutor = toolExecutor;
        _toolRegistry = toolRegistry;
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

            if (RequiresSession(method))
            {
                if (!context.Request.Headers.TryGetValue("Mcp-Session-Id", out var sessionHeader) ||
                    string.IsNullOrWhiteSpace(sessionHeader))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await WriteJsonRpcError(context, id, -32600, "Missing Mcp-Session-Id header");
                    return;
                }

                var sessionId = sessionHeader.ToString();
                if (!_sessions.ContainsKey(sessionId))
                {
                    _logger.LogWarning("Unknown session ID: {SessionId}", sessionId);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await WriteJsonRpcError(context, id, -32600, "Unknown session");
                    return;
                }
            }

            _logger.LogDebug("MCP request: method={Method}, id={Id}", method, id);

            switch (method)
            {
                case "initialize":
                    await HandleInitialize(context, id);
                    break;
                case "tools/list":
                    await HandleToolsList(context, id);
                    break;
                case "catalog/list":
                case "capabilities/catalog":
                    await HandleCatalog(context, id);
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
        // Create session
        var sessionId = Guid.NewGuid().ToString("N");
        _sessions.TryAdd(sessionId, DateTimeOffset.UtcNow);
        context.Response.Headers["Mcp-Session-Id"] = sessionId;

        _logger.LogInformation("MCP session initialized: {SessionId}", sessionId);

        var result = new
        {
            protocolVersion = "2025-06-18",
            capabilities = new
            {
                tools = new { listChanged = true },
                catalog = new { list = true }
            },
            serverInfo = new
            {
                name = "graphql-mcp",
                version = typeof(StreamableHttpTransport).Assembly.GetName().Version?.ToString(3) ?? "0.1.0"
            }
        };

        await WriteJsonRpcResult(context, id, JsonSerializer.Serialize(result, JsonOptions));
    }

    private async Task HandleToolsList(HttpContext context, object? id)
    {
        var tools = _toolRegistry.Tools.Select(t => new
        {
            name = t.Name,
            description = t.Description,
            annotations = new
            {
                domain = t.Domain,
                category = t.Category,
                tags = t.Tags
            },
            inputSchema = JsonSerializer.Deserialize<object>(
                t.InputSchema.RootElement.GetRawText(), JsonOptions)
        });

        var result = new { tools };
        await WriteJsonRpcResult(context, id, JsonSerializer.Serialize(result, JsonOptions));
    }

    private async Task HandleCatalog(HttpContext context, object? id)
    {
        var domains = _toolRegistry.Tools
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Domain) ? "general" : t.Domain, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var categories = group
                    .Select(t => t.Category)
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(category => category, StringComparer.Ordinal)
                    .ToArray();

                var tags = group
                    .SelectMany(t => t.Tags)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(tag => tag, StringComparer.Ordinal)
                    .ToArray();

                return new
                {
                    domain = group.Key,
                    categories,
                    tags,
                    toolCount = group.Count(),
                    toolNames = group.Select(t => t.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                    tools = group
                        .OrderBy(t => t.Name, StringComparer.Ordinal)
                        .Select(t => new
                        {
                            name = t.Name,
                            description = t.Description,
                            category = t.Category,
                            operationType = t.OperationType.ToString().ToLowerInvariant(),
                            fieldName = t.GraphQLFieldName,
                            tags = t.Tags
                        })
                        .ToArray()
                };
            })
            .OrderBy(entry => entry.domain, StringComparer.Ordinal)
            .ToArray();

        var result = new
        {
            domainCount = domains.Length,
            toolCount = _toolRegistry.Tools.Count,
            domains
        };
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

        var executionResult = await _toolExecutor.ExecuteAsync(
            toolName, arguments, headers, context.RequestAborted);

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

    private static bool RequiresSession(string? method) =>
        method is "tools/list" or "catalog/list" or "capabilities/catalog" or "tools/call";

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
