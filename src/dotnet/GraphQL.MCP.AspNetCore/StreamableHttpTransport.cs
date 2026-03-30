using System.Collections.Concurrent;
using System.Text;
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
                case "catalog/search":
                case "capabilities/search":
                    await HandleCatalogSearch(context, doc.RootElement, id);
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
                catalog = new { list = true, search = true }
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
                tags = t.Tags,
                semanticHints = t.SemanticHints
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

                var semanticKeywords = group
                    .SelectMany(t => t.SemanticHints.Keywords)
                    .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(keyword => keyword, StringComparer.Ordinal)
                    .ToArray();

                var semanticIntents = group
                    .Select(t => t.SemanticHints.Intent)
                    .Where(intent => !string.IsNullOrWhiteSpace(intent))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(intent => intent, StringComparer.Ordinal)
                    .ToArray();

                return new
                {
                    domain = group.Key,
                    categories,
                    tags,
                    semanticHints = new
                    {
                        intents = semanticIntents,
                        keywords = semanticKeywords
                    },
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
                            tags = t.Tags,
                            semanticHints = t.SemanticHints
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

    private async Task HandleCatalogSearch(HttpContext context, JsonElement root, object? id)
    {
        var search = ParseCatalogSearchRequest(root);
        var allMatches = _toolRegistry.Tools
            .Select(tool => new { Tool = tool, Score = ScoreTool(tool, search) })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Tool.Name, StringComparer.Ordinal)
            .ToArray();

        var matches = allMatches
            .Take(search.Limit)
            .Select(entry => new
            {
                name = entry.Tool.Name,
                description = entry.Tool.Description,
                domain = entry.Tool.Domain,
                category = entry.Tool.Category,
                operationType = entry.Tool.OperationType.ToString().ToLowerInvariant(),
                fieldName = entry.Tool.GraphQLFieldName,
                tags = entry.Tool.Tags,
                semanticHints = entry.Tool.SemanticHints,
                score = entry.Score
            })
            .ToArray();

        var domains = allMatches
            .Select(entry => new
            {
                name = entry.Tool.Name,
                domain = entry.Tool.Domain,
                tags = entry.Tool.Tags
            })
            .GroupBy(match => match.domain, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                domain = group.Key,
                toolCount = group.Count(),
                toolNames = group.Select(match => match.name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                tags = group
                    .SelectMany(match => match.tags)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(tag => tag, StringComparer.Ordinal)
                    .ToArray()
            })
            .OrderBy(group => group.domain, StringComparer.Ordinal)
            .ToArray();

        var result = new
        {
            query = search.Query,
            filters = new
            {
                domain = search.Domain,
                category = search.Category,
                operationType = search.OperationType,
                tags = search.Tags
            },
            totalMatches = allMatches.Length,
            domainCount = domains.Length,
            matches,
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
        method is "tools/list" or "catalog/list" or "capabilities/catalog" or "catalog/search" or "capabilities/search" or "tools/call";

    private static CatalogSearchRequest ParseCatalogSearchRequest(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var paramsElement) || paramsElement.ValueKind != JsonValueKind.Object)
        {
            return new CatalogSearchRequest(null, null, null, null, [], 20);
        }

        var query = GetOptionalString(paramsElement, "query");
        var domain = GetOptionalString(paramsElement, "domain");
        var category = GetOptionalString(paramsElement, "category");
        var operationType = GetOptionalString(paramsElement, "operationType");
        var tags = GetOptionalStringArray(paramsElement, "tags");
        var limit = GetOptionalInt(paramsElement, "limit");
        if (limit <= 0)
        {
            limit = 20;
        }

        return new CatalogSearchRequest(query, domain, category, operationType, tags, Math.Min(limit, 100));
    }

    private static int ScoreTool(McpToolDescriptor tool, CatalogSearchRequest search)
    {
        if (!MatchesFilter(tool.Domain, search.Domain) ||
            !MatchesFilter(tool.Category, search.Category) ||
            !MatchesFilter(tool.OperationType.ToString(), search.OperationType) ||
            !MatchesTags(tool.Tags, search.Tags))
        {
            return 0;
        }

        var queryTokens = TokenizeSearchText(search.Query);
        if (queryTokens.Count == 0)
        {
            return 1;
        }

        var searchable = BuildSearchableText(tool);
        var exactValues = BuildExactMatchSet(tool);
        var total = 0;

        foreach (var token in queryTokens)
        {
            var tokenScore = 0;
            if (exactValues.Contains(token))
            {
                tokenScore = 40;
            }
            else if (searchable.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                tokenScore = 15;
            }

            if (tokenScore == 0)
            {
                return 0;
            }

            total += tokenScore;
        }

        return total;
    }

    private static HashSet<string> BuildExactMatchSet(McpToolDescriptor tool)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeSearchValue(tool.Name),
            NormalizeSearchValue(tool.GraphQLFieldName),
            NormalizeSearchValue(tool.Domain),
            NormalizeSearchValue(tool.Category),
            NormalizeSearchValue(tool.OperationType.ToString())
        };

        foreach (var tag in tool.Tags)
        {
            values.Add(NormalizeSearchValue(tag));
        }

        foreach (var keyword in tool.SemanticHints.Keywords)
        {
            values.Add(NormalizeSearchValue(keyword));
        }

        return values;
    }

    private static string[] BuildSearchableText(McpToolDescriptor tool) =>
    [
        tool.Name,
        tool.GraphQLFieldName,
        tool.Description ?? "",
        tool.Domain,
        tool.Category ?? "",
        tool.SemanticHints.Intent,
        string.Join(" ", tool.Tags),
        string.Join(" ", tool.SemanticHints.Keywords)
    ];

    private static bool MatchesFilter(string? actual, string? expected) =>
        string.IsNullOrWhiteSpace(expected) ||
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesTags(IReadOnlyList<string> actualTags, IReadOnlyList<string> requiredTags)
    {
        if (requiredTags.Count == 0)
        {
            return true;
        }

        return requiredTags.All(required =>
            actualTags.Any(actual => string.Equals(actual, required, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> TokenizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string NormalizeSearchValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static IReadOnlyList<string> GetOptionalStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var single = property.GetString();
            return string.IsNullOrWhiteSpace(single) ? [] : [single];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    private static int GetOptionalInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        return property.TryGetInt32(out var intValue) ? intValue : 0;
    }

    private sealed record CatalogSearchRequest(
        string? Query,
        string? Domain,
        string? Category,
        string? OperationType,
        IReadOnlyList<string> Tags,
        int Limit);

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
