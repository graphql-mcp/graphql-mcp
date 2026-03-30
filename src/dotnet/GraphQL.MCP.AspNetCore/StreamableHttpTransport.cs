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
                case "prompts/list":
                    await HandlePromptsList(context, id);
                    break;
                case "prompts/get":
                    await HandlePromptGet(context, doc.RootElement, id);
                    break;
                case "resources/list":
                    await HandleResourcesList(context, id);
                    break;
                case "resources/read":
                    await HandleResourcesRead(context, doc.RootElement, id);
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
                prompts = new { listChanged = true },
                resources = new { listChanged = true, read = true },
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
        var result = BuildCatalogSummary();
        await WriteJsonRpcResult(context, id, JsonSerializer.Serialize(result, JsonOptions));
    }

    private async Task HandleResourcesList(HttpContext context, object? id)
    {
        var catalog = BuildCatalogSummary();
        var resources = new List<ResourceSummary>
        {
            new(
                CatalogOverviewUri,
                "Catalog Overview",
                "Grouped discovery summary for all published GraphQL MCP tools.",
                "application/json")
        };

        resources.AddRange(catalog.Domains.Select(domain => new ResourceSummary(
            $"{CatalogDomainUriPrefix}{Uri.EscapeDataString(domain.Domain)}",
            $"Domain Summary: {domain.Domain}",
            $"Discovery summary for the '{domain.Domain}' domain.",
            "application/json")));

        var result = new { resources };
        await WriteJsonRpcResult(context, id, JsonSerializer.Serialize(result, JsonOptions));
    }

    private async Task HandlePromptsList(HttpContext context, object? id)
    {
        var prompts = BuildPromptDefinitions()
            .Select(prompt => new
            {
                name = prompt.Name,
                title = prompt.Title,
                description = prompt.Description,
                arguments = prompt.Arguments.Select(argument => new
                {
                    name = argument.Name,
                    description = argument.Description,
                    required = argument.Required
                })
            });

        var result = new { prompts };
        await WriteJsonRpcResult(context, id, JsonSerializer.Serialize(result, JsonOptions));
    }

    private async Task HandlePromptGet(HttpContext context, JsonElement root, object? id)
    {
        if (!root.TryGetProperty("params", out var paramsElement) ||
            paramsElement.ValueKind != JsonValueKind.Object ||
            !paramsElement.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            await WriteJsonRpcError(context, id, -32602, "Missing prompt name");
            return;
        }

        var promptName = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(promptName))
        {
            await WriteJsonRpcError(context, id, -32602, "Missing prompt name");
            return;
        }

        var arguments = paramsElement.TryGetProperty("arguments", out var argumentsElement) &&
                        argumentsElement.ValueKind == JsonValueKind.Object
            ? argumentsElement
            : default;

        try
        {
            var prompt = BuildPromptResult(promptName, arguments);
            if (prompt is null)
            {
                await WriteJsonRpcError(context, id, -32602, $"Unknown prompt: {promptName}");
                return;
            }

            await WriteJsonRpcResult(context, id, JsonSerializer.Serialize(prompt, JsonOptions));
        }
        catch (InvalidOperationException ex)
        {
            await WriteJsonRpcError(context, id, -32602, ex.Message);
        }
    }

    private async Task HandleResourcesRead(HttpContext context, JsonElement root, object? id)
    {
        if (!root.TryGetProperty("params", out var paramsElement) ||
            paramsElement.ValueKind != JsonValueKind.Object ||
            !paramsElement.TryGetProperty("uri", out var uriElement) ||
            uriElement.ValueKind != JsonValueKind.String)
        {
            await WriteJsonRpcError(context, id, -32602, "Missing resource uri");
            return;
        }

        var uri = uriElement.GetString();
        if (string.IsNullOrWhiteSpace(uri))
        {
            await WriteJsonRpcError(context, id, -32602, "Missing resource uri");
            return;
        }

        var resourceText = TryBuildResourceContent(uri);
        if (resourceText is null)
        {
            await WriteJsonRpcError(context, id, -32602, $"Unknown resource: {uri}");
            return;
        }

        var result = new
        {
            contents = new[]
            {
                new
                {
                    uri,
                    mimeType = "application/json",
                    text = resourceText
                }
            }
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
        method is "tools/list" or "prompts/list" or "prompts/get" or "resources/list" or "resources/read" or "catalog/list" or "capabilities/catalog" or "catalog/search" or "capabilities/search" or "tools/call";

    private CatalogSummary BuildCatalogSummary()
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
                    .Cast<string>()
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

                var tools = group
                    .OrderBy(t => t.Name, StringComparer.Ordinal)
                    .Select(t => new CatalogToolSummary(
                        t.Name,
                        t.Description,
                        t.Category,
                        t.OperationType.ToString().ToLowerInvariant(),
                        t.GraphQLFieldName,
                        t.Tags,
                        t.SemanticHints))
                    .ToArray();

                return new CatalogDomainSummary(
                    group.Key,
                    categories,
                    tags,
                    new CatalogSemanticHints(semanticIntents, semanticKeywords),
                    group.Count(),
                    group.Select(t => t.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                    tools);
            })
            .OrderBy(entry => entry.Domain, StringComparer.Ordinal)
            .ToArray();

        return new CatalogSummary(domains.Length, _toolRegistry.Tools.Count, domains);
    }

    private string? TryBuildResourceContent(string uri)
    {
        var catalog = BuildCatalogSummary();

        if (string.Equals(uri, CatalogOverviewUri, StringComparison.OrdinalIgnoreCase))
        {
            var overview = new
            {
                kind = "catalogOverview",
                serverInfo = new
                {
                    name = "graphql-mcp",
                    version = typeof(StreamableHttpTransport).Assembly.GetName().Version?.ToString(3) ?? "0.1.0"
                },
                capabilities = new
                {
                    tools = new { listChanged = true },
                    resources = new { listChanged = true, read = true },
                    catalog = new { list = true, search = true }
                },
                domainCount = catalog.DomainCount,
                toolCount = catalog.ToolCount,
                domains = catalog.Domains
            };

            return JsonSerializer.Serialize(overview, JsonOptions);
        }

        if (uri.StartsWith(CatalogDomainUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var encodedDomain = uri[CatalogDomainUriPrefix.Length..];
            var domainName = Uri.UnescapeDataString(encodedDomain);
            var domain = catalog.Domains.FirstOrDefault(entry =>
                string.Equals(entry.Domain, domainName, StringComparison.OrdinalIgnoreCase));
            if (domain is null)
            {
                return null;
            }

            var summary = new
            {
                kind = "domainSummary",
                domain = domain.Domain,
                categories = domain.Categories,
                tags = domain.Tags,
                semanticHints = domain.SemanticHints,
                toolCount = domain.ToolCount,
                toolNames = domain.ToolNames,
                tools = domain.Tools
            };

            return JsonSerializer.Serialize(summary, JsonOptions);
        }

        return null;
    }

    private IReadOnlyList<PromptDefinition> BuildPromptDefinitions() =>
    [
        new(
            "explore_catalog",
            "Explore Catalog",
            "Review the catalog overview resource and summarize the available domains, categories, and next discovery steps.",
            []),
        new(
            "explore_domain",
            "Explore Domain",
            "Review a specific domain summary resource and explain the most relevant tools for that domain.",
            [new PromptArgumentDefinition("domain", "Domain name from catalog/list or resources/list.", true)]),
        new(
            "choose_tool_for_task",
            "Choose Tool For Task",
            "Use the discovery metadata to recommend the best tool for a task and explain the required arguments.",
            [
                new PromptArgumentDefinition("task", "Plain-language task or goal to match against the catalog.", true),
                new PromptArgumentDefinition("domain", "Optional domain to narrow the prompt to a known group.", false)
            ])
    ];

    private object? BuildPromptResult(string promptName, JsonElement arguments)
    {
        return promptName switch
        {
            "explore_catalog" => new
            {
                description = "Explore the full catalog overview before choosing a tool.",
                messages = BuildPromptMessages(
                    "Review the embedded catalog overview. Summarize the available domains, highlight the most likely starting points, and suggest 2-3 next catalog or tool actions before executing anything.",
                    CatalogOverviewUri)
            },
            "explore_domain" => BuildExploreDomainPrompt(arguments),
            "choose_tool_for_task" => BuildChooseToolPrompt(arguments),
            _ => null
        };
    }

    private object BuildExploreDomainPrompt(JsonElement arguments)
    {
        var domain = GetPromptArgument(arguments, "domain", required: true)
            ?? throw new InvalidOperationException("Missing required prompt argument: domain");
        var resourceUri = $"{CatalogDomainUriPrefix}{Uri.EscapeDataString(domain)}";
        EnsureResourceExists(resourceUri);

        return new
        {
            description = $"Explore the '{domain}' domain and recommend the best next tool choices.",
            messages = BuildPromptMessages(
                $"Review the embedded domain summary for '{domain}'. Explain the domain's available tools, identify the strongest candidates for common tasks, and point out any arguments a client should gather before calling a tool.",
                resourceUri)
        };
    }

    private object BuildChooseToolPrompt(JsonElement arguments)
    {
        var task = GetPromptArgument(arguments, "task", required: true)
            ?? throw new InvalidOperationException("Missing required prompt argument: task");
        var domain = GetPromptArgument(arguments, "domain", required: false);
        var resourceUri = string.IsNullOrWhiteSpace(domain)
            ? CatalogOverviewUri
            : $"{CatalogDomainUriPrefix}{Uri.EscapeDataString(domain)}";

        EnsureResourceExists(resourceUri);

        var promptText = string.IsNullOrWhiteSpace(domain)
            ? $"A user wants to: {task}\n\nReview the embedded catalog overview and recommend the best tool to call next. Explain why it fits, what arguments are likely required, and whether the client should narrow further with catalog/search before executing."
            : $"A user wants to: {task}\n\nThe likely domain is '{domain}'. Review the embedded domain summary and recommend the best tool to call next. Explain why it fits, what arguments are likely required, and whether the client should still use catalog/search before executing.";

        return new
        {
            description = "Recommend the most relevant tool for a task using the discovery summaries.",
            messages = BuildPromptMessages(promptText, resourceUri)
        };
    }

    private object[] BuildPromptMessages(string instruction, string resourceUri)
    {
        var resourceText = TryBuildResourceContent(resourceUri);
        if (resourceText is null)
        {
            throw new InvalidOperationException($"Unknown resource: {resourceUri}");
        }

        return
        [
            new
            {
                role = "user",
                content = new
                {
                    type = "text",
                    text = instruction
                }
            },
            new
            {
                role = "user",
                content = new
                {
                    type = "resource",
                    resource = new
                    {
                        uri = resourceUri,
                        mimeType = "application/json",
                        text = resourceText
                    }
                }
            }
        ];
    }

    private void EnsureResourceExists(string resourceUri)
    {
        if (TryBuildResourceContent(resourceUri) is null)
        {
            throw new InvalidOperationException($"Unknown resource: {resourceUri}");
        }
    }

    private static string? GetPromptArgument(JsonElement arguments, string name, bool required)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            if (required)
            {
                throw new InvalidOperationException($"Missing required prompt argument: {name}");
            }

            return null;
        }

        return property.GetString();
    }

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

    private sealed record CatalogSummary(
        int DomainCount,
        int ToolCount,
        IReadOnlyList<CatalogDomainSummary> Domains);

    private sealed record CatalogDomainSummary(
        string Domain,
        IReadOnlyList<string> Categories,
        IReadOnlyList<string> Tags,
        CatalogSemanticHints SemanticHints,
        int ToolCount,
        IReadOnlyList<string> ToolNames,
        IReadOnlyList<CatalogToolSummary> Tools);

    private sealed record CatalogToolSummary(
        string Name,
        string? Description,
        string? Category,
        string OperationType,
        string FieldName,
        IReadOnlyList<string> Tags,
        McpSemanticHints SemanticHints);

    private sealed record CatalogSemanticHints(
        IReadOnlyList<string> Intents,
        IReadOnlyList<string> Keywords);

    private sealed record ResourceSummary(
        string Uri,
        string Name,
        string Description,
        string MimeType);

    private sealed record PromptDefinition(
        string Name,
        string Title,
        string Description,
        IReadOnlyList<PromptArgumentDefinition> Arguments);

    private sealed record PromptArgumentDefinition(
        string Name,
        string Description,
        bool Required);

    private const string CatalogOverviewUri = "graphql-mcp://catalog/overview";
    private const string CatalogDomainUriPrefix = "graphql-mcp://catalog/domain/";

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
