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

    /// <summary>
    /// Handles the OAuth authorization-server metadata route exposed next to the MCP endpoint.
    /// </summary>
    public async Task HandleOAuthAuthorizationServerMetadataAsync(HttpContext context)
    {
        var metadata = BuildOAuthAuthorizationServerMetadata();
        if (metadata is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(metadata, JsonOptions));
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
                catalog = new { list = true, search = true },
                authorization = BuildAuthorizationCapability()
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
        var resources = BuildResourceSummaries(catalog);

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

    private IReadOnlyList<ResourceSummary> BuildResourceSummaries(CatalogSummary catalog)
    {
        var resources = new List<ResourceSummary>
        {
            new(
                CatalogOverviewUri,
                "Catalog Overview",
                "Grouped discovery summary for all published GraphQL MCP tools.",
                "application/json")
        };

        if (ShouldPublishAuthorizationMetadata())
        {
            resources.Add(new ResourceSummary(
                AuthorizationMetadataUri,
                "Authorization Metadata",
                "OAuth metadata and required scopes for authenticated MCP clients.",
                "application/json"));
        }

        resources.AddRange(BuildDiscoveryPackDefinitions().Select(pack => new ResourceSummary(
            $"{DiscoveryPackUriPrefix}{pack.Name}",
            $"Discovery Pack: {pack.Title}",
            pack.Description,
            "application/json")));

        resources.AddRange(catalog.Domains.Select(domain => new ResourceSummary(
            $"{CatalogDomainUriPrefix}{Uri.EscapeDataString(domain.Domain)}",
            $"Domain Summary: {domain.Domain}",
            $"Discovery summary for the '{domain.Domain}' domain.",
            "application/json")));

        resources.AddRange(_toolRegistry.Tools
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(tool => new ResourceSummary(
                $"{CatalogToolUriPrefix}{Uri.EscapeDataString(tool.Name)}",
                $"Tool Summary: {tool.Name}",
                $"Execution-oriented summary for the '{tool.Name}' tool.",
                "application/json")));

        return resources;
    }

    private object BuildCatalogOverviewResource(CatalogSummary catalog) => new
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
            prompts = new { listChanged = true },
            resources = new { listChanged = true, read = true },
            catalog = new { list = true, search = true },
            authorization = BuildAuthorizationCapability()
        },
        domainCount = catalog.DomainCount,
        toolCount = catalog.ToolCount,
        domains = catalog.Domains
    };

    private object BuildAuthorizationCapability() => new
    {
        mode = _options.Authorization.Mode.ToString().ToLowerInvariant(),
        requiredScopes = _options.Authorization.RequiredScopes,
        oauth2 = new
        {
            metadata = ShouldPublishAuthorizationMetadata(),
            resource = ShouldPublishAuthorizationMetadata() ? AuthorizationMetadataUri : null,
            wellKnownPath = ShouldPublishAuthorizationMetadata() ? AuthorizationMetadataWellKnownPath : null
        }
    };

    private bool ShouldPublishAuthorizationMetadata()
    {
        var metadata = _options.Authorization.Metadata;
        return _options.Authorization.Mode != McpAuthMode.None ||
               _options.Authorization.RequiredScopes.Count > 0 ||
               !string.IsNullOrWhiteSpace(metadata.Issuer) ||
               !string.IsNullOrWhiteSpace(metadata.AuthorizationEndpoint) ||
               !string.IsNullOrWhiteSpace(metadata.TokenEndpoint) ||
               !string.IsNullOrWhiteSpace(metadata.RegistrationEndpoint) ||
               !string.IsNullOrWhiteSpace(metadata.JwksUri) ||
               !string.IsNullOrWhiteSpace(metadata.ServiceDocumentation);
    }

    private object? BuildAuthorizationMetadataResource()
    {
        if (!ShouldPublishAuthorizationMetadata())
        {
            return null;
        }

        var metadata = _options.Authorization.Metadata;
        return new
        {
            kind = "authorizationMetadata",
            mode = _options.Authorization.Mode.ToString().ToLowerInvariant(),
            requiredScopes = _options.Authorization.RequiredScopes,
            resource = AuthorizationMetadataUri,
            wellKnownPath = AuthorizationMetadataWellKnownPath,
            oauth2 = new
            {
                issuer = metadata.Issuer,
                authorizationEndpoint = metadata.AuthorizationEndpoint,
                tokenEndpoint = metadata.TokenEndpoint,
                registrationEndpoint = metadata.RegistrationEndpoint,
                jwksUri = metadata.JwksUri,
                serviceDocumentation = metadata.ServiceDocumentation,
                responseTypesSupported = metadata.ResponseTypesSupported,
                grantTypesSupported = metadata.GrantTypesSupported,
                tokenEndpointAuthMethodsSupported = metadata.TokenEndpointAuthMethodsSupported,
                scopesSupported = _options.Authorization.RequiredScopes
            }
        };
    }

    private IDictionary<string, object?>? BuildOAuthAuthorizationServerMetadata()
    {
        if (!ShouldPublishAuthorizationMetadata())
        {
            return null;
        }

        var metadata = _options.Authorization.Metadata;
        var document = new Dictionary<string, object?>
        {
            ["scopes_supported"] = _options.Authorization.RequiredScopes,
            ["response_types_supported"] = metadata.ResponseTypesSupported,
            ["grant_types_supported"] = metadata.GrantTypesSupported,
            ["token_endpoint_auth_methods_supported"] = metadata.TokenEndpointAuthMethodsSupported,
            ["x_graphql_mcp"] = new Dictionary<string, object?>
            {
                ["mode"] = _options.Authorization.Mode.ToString().ToLowerInvariant(),
                ["required_scopes"] = _options.Authorization.RequiredScopes,
                ["resource_uri"] = AuthorizationMetadataUri,
                ["well_known_path"] = AuthorizationMetadataWellKnownPath
            }
        };

        AddIfNotBlank(document, "issuer", metadata.Issuer);
        AddIfNotBlank(document, "authorization_endpoint", metadata.AuthorizationEndpoint);
        AddIfNotBlank(document, "token_endpoint", metadata.TokenEndpoint);
        AddIfNotBlank(document, "registration_endpoint", metadata.RegistrationEndpoint);
        AddIfNotBlank(document, "jwks_uri", metadata.JwksUri);
        AddIfNotBlank(document, "service_documentation", metadata.ServiceDocumentation);
        return document;
    }

    private object? BuildDomainResource(CatalogSummary catalog, string domainName)
    {
        var domain = catalog.Domains.FirstOrDefault(entry =>
            string.Equals(entry.Domain, domainName, StringComparison.OrdinalIgnoreCase));
        if (domain is null)
        {
            return null;
        }

        return new
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
    }

    private object? BuildToolResource(string toolName)
    {
        var tool = _toolRegistry.Tools.FirstOrDefault(entry =>
            string.Equals(entry.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            return null;
        }

        var schema = JsonSerializer.Deserialize<object>(tool.InputSchema.RootElement.GetRawText(), JsonOptions);
        var requiredArguments = GetSchemaStringArray(tool.InputSchema.RootElement, "required");
        var optionalArguments = GetSchemaPropertyNames(tool.InputSchema.RootElement)
            .Where(name => !requiredArguments.Contains(name, StringComparer.Ordinal))
            .ToArray();

        return new
        {
            kind = "toolSummary",
            name = tool.Name,
            description = tool.Description,
            domain = tool.Domain,
            category = tool.Category,
            operationType = tool.OperationType.ToString().ToLowerInvariant(),
            fieldName = tool.GraphQLFieldName,
            tags = tool.Tags,
            semanticHints = tool.SemanticHints,
            requiredArguments,
            optionalArguments,
            argumentMapping = tool.ArgumentMapping,
            inputSchema = schema
        };
    }

    private object? BuildDiscoveryPackResource(string packName)
    {
        return packName.ToLowerInvariant() switch
        {
            "start-here" => new
            {
                kind = "resourcePack",
                pack = "start-here",
                title = "Discovery Pack: Start Here",
                description = "A reusable exploration playbook for unfamiliar schemas or tasks.",
                whenToUse = new[]
                {
                    "You are new to the schema or domain.",
                    "You need to map a broad task to the right domain or tool."
                },
                recommendedPrompts = new[] { "explore_catalog", "plan_task_workflow", "choose_tool_for_task" },
                recommendedResources = new[] { CatalogOverviewUri },
                steps = new[]
                {
                    BuildPackStep(1, "initialize", "initialize", null, null, "Start the MCP session and discover supported capabilities."),
                    BuildPackStep(2, "review_overview", "resources/read", CatalogOverviewUri, null, "Scan grouped domains, categories, and discovery metadata."),
                    BuildPackStep(3, "inspect_groups", "catalog/list", null, null, "Review grouped domains before narrowing further."),
                    BuildPackStep(4, "narrow_candidates", "catalog/search", null, null, "Search by task keywords, domain, or tags when multiple tools look plausible."),
                    BuildPackStep(5, "choose_flow", "prompts/get", null, "choose_tool_for_task", "Let the client explain the best next tool before executing anything.")
                }
            },
            "investigate-domain" => new
            {
                kind = "resourcePack",
                pack = "investigate-domain",
                title = "Discovery Pack: Investigate Domain",
                description = "A reusable playbook for drilling into one domain and comparing candidate tools.",
                whenToUse = new[]
                {
                    "You already know the likely domain.",
                    "You need to compare multiple tools inside one domain."
                },
                recommendedPrompts = new[] { "explore_domain", "compare_tools_for_task", "plan_task_workflow" },
                recommendedResources = new[] { "graphql-mcp://catalog/domain/<domain>" },
                steps = new[]
                {
                    BuildPackStep(1, "read_domain_summary", "resources/read", "graphql-mcp://catalog/domain/<domain>", null, "Review categories, tags, semantic hints, and available tools for the domain."),
                    BuildPackStep(2, "domain_search", "catalog/search", null, null, "Search within the domain when the summary still contains multiple plausible tools."),
                    BuildPackStep(3, "compare_candidates", "prompts/get", null, "compare_tools_for_task", "Ask the client to compare the best candidate tools and call out trade-offs."),
                    BuildPackStep(4, "inspect_tool_summary", "resources/read", "graphql-mcp://catalog/tool/<tool>", null, "Read the tool summary once a likely candidate emerges.")
                }
            },
            "safe-tool-call" => new
            {
                kind = "resourcePack",
                pack = "safe-tool-call",
                title = "Discovery Pack: Safe Tool Call",
                description = "A reusable execution checklist before calling an MCP tool.",
                whenToUse = new[]
                {
                    "You have selected a likely tool and need to confirm arguments.",
                    "You want to avoid premature or unsafe execution."
                },
                recommendedPrompts = new[] { "prepare_tool_call", "choose_tool_for_task" },
                recommendedResources = new[] { "graphql-mcp://catalog/tool/<tool>" },
                checklist = new[]
                {
                    "Confirm the tool is the right match for the task.",
                    "List required arguments and note any missing user input.",
                    "Review optional filters that could narrow the result safely.",
                    "Decide whether another catalog/search step is needed before execution."
                },
                steps = new[]
                {
                    BuildPackStep(1, "read_tool_summary", "resources/read", "graphql-mcp://catalog/tool/<tool>", null, "Inspect required arguments, optional arguments, and semantic hints."),
                    BuildPackStep(2, "prepare_execution", "prompts/get", null, "prepare_tool_call", "Have the client produce a safe execution plan before tools/call."),
                    BuildPackStep(3, "execute_tool", "tools/call", null, null, "Call the tool once arguments and ambiguities are resolved.")
                }
            },
            _ => null
        };
    }

    private static IReadOnlyList<ResourcePackDefinition> BuildDiscoveryPackDefinitions() =>
    [
        new("start-here", "Start Here", "Reusable exploration playbook for unfamiliar tasks or schemas."),
        new("investigate-domain", "Investigate Domain", "Reusable playbook for drilling into one domain and comparing tools."),
        new("safe-tool-call", "Safe Tool Call", "Reusable execution checklist before calling a tool.")
    ];

    private static ResourcePackStep BuildPackStep(
        int order,
        string action,
        string method,
        string? target,
        string? prompt,
        string purpose) =>
        new(order, action, method, target, prompt, purpose);

    private string? TryBuildResourceContent(string uri)
    {
        var catalog = BuildCatalogSummary();
        var resource = TryBuildResourceObject(uri, catalog);
        return resource is null ? null : JsonSerializer.Serialize(resource, JsonOptions);
    }

    private object? TryBuildResourceObject(string uri, CatalogSummary catalog)
    {
        if (string.Equals(uri, CatalogOverviewUri, StringComparison.OrdinalIgnoreCase))
        {
            return BuildCatalogOverviewResource(catalog);
        }

        if (string.Equals(uri, AuthorizationMetadataUri, StringComparison.OrdinalIgnoreCase))
        {
            return BuildAuthorizationMetadataResource();
        }

        if (uri.StartsWith(CatalogDomainUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var encodedDomain = uri[CatalogDomainUriPrefix.Length..];
            var domainName = Uri.UnescapeDataString(encodedDomain);
            return BuildDomainResource(catalog, domainName);
        }

        if (uri.StartsWith(CatalogToolUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var encodedToolName = uri[CatalogToolUriPrefix.Length..];
            var toolName = Uri.UnescapeDataString(encodedToolName);
            return BuildToolResource(toolName);
        }

        if (uri.StartsWith(DiscoveryPackUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var encodedPackName = uri[DiscoveryPackUriPrefix.Length..];
            var packName = Uri.UnescapeDataString(encodedPackName);
            return BuildDiscoveryPackResource(packName);
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
            ]),
        new(
            "plan_task_workflow",
            "Plan Task Workflow",
            "Compose a discovery plan for a task using the reusable playbooks and catalog summaries.",
            [
                new PromptArgumentDefinition("task", "Plain-language task or goal to plan around.", true),
                new PromptArgumentDefinition("domain", "Optional domain to focus the workflow on.", false)
            ]),
        new(
            "compare_tools_for_task",
            "Compare Tools For Task",
            "Compare the best candidate tools for a task before execution.",
            [
                new PromptArgumentDefinition("task", "Plain-language task or goal to compare candidate tools for.", true),
                new PromptArgumentDefinition("domain", "Optional domain to narrow the comparison.", false)
            ]),
        new(
            "prepare_tool_call",
            "Prepare Tool Call",
            "Review a tool summary and safe-call playbook before executing a specific tool.",
            [
                new PromptArgumentDefinition("tool", "Published MCP tool name to prepare for execution.", true),
                new PromptArgumentDefinition("task", "Optional task context for the planned call.", false)
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
            "plan_task_workflow" => BuildPlanTaskWorkflowPrompt(arguments),
            "compare_tools_for_task" => BuildCompareToolsPrompt(arguments),
            "prepare_tool_call" => BuildPrepareToolCallPrompt(arguments),
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

    private object BuildPlanTaskWorkflowPrompt(JsonElement arguments)
    {
        var task = GetPromptArgument(arguments, "task", required: true)
            ?? throw new InvalidOperationException("Missing required prompt argument: task");
        var domain = GetPromptArgument(arguments, "domain", required: false);
        var summaryResourceUri = string.IsNullOrWhiteSpace(domain)
            ? CatalogOverviewUri
            : $"{CatalogDomainUriPrefix}{Uri.EscapeDataString(domain)}";

        EnsureResourceExists(summaryResourceUri);

        var instruction = string.IsNullOrWhiteSpace(domain)
            ? $"A user wants to: {task}\n\nUse the embedded discovery playbook and catalog overview to propose the best step-by-step exploration workflow. Explain when to use resources/read, catalog/search, prompts/get, and tools/call, and identify what information the client should gather before execution."
            : $"A user wants to: {task}\n\nThe likely domain is '{domain}'. Use the embedded domain investigation playbook and domain summary to propose the best step-by-step workflow. Explain when to read resources, search inside the domain, compare tools, and what arguments should be gathered before execution.";

        return new
        {
            description = "Plan a reusable discovery workflow for a task using the advanced discovery packs.",
            messages = BuildPromptMessages(
                instruction,
                $"{DiscoveryPackUriPrefix}{(string.IsNullOrWhiteSpace(domain) ? "start-here" : "investigate-domain")}",
                summaryResourceUri)
        };
    }

    private object BuildCompareToolsPrompt(JsonElement arguments)
    {
        var task = GetPromptArgument(arguments, "task", required: true)
            ?? throw new InvalidOperationException("Missing required prompt argument: task");
        var domain = GetPromptArgument(arguments, "domain", required: false);
        var summaryResourceUri = string.IsNullOrWhiteSpace(domain)
            ? CatalogOverviewUri
            : $"{CatalogDomainUriPrefix}{Uri.EscapeDataString(domain)}";

        EnsureResourceExists(summaryResourceUri);

        var instruction = string.IsNullOrWhiteSpace(domain)
            ? $"A user wants to: {task}\n\nUse the embedded discovery pack and catalog summary to compare the 2-3 best candidate tools. Explain the trade-offs between them, when catalog/search should be used first, and what arguments or filters are likely needed before execution."
            : $"A user wants to: {task}\n\nThe likely domain is '{domain}'. Use the embedded discovery pack and domain summary to compare the strongest candidate tools in that domain. Explain the trade-offs, expected arguments, and the safest next step before a tool call.";

        return new
        {
            description = "Compare likely candidate tools for a task before choosing one to execute.",
            messages = BuildPromptMessages(
                instruction,
                $"{DiscoveryPackUriPrefix}{(string.IsNullOrWhiteSpace(domain) ? "start-here" : "investigate-domain")}",
                summaryResourceUri)
        };
    }

    private object BuildPrepareToolCallPrompt(JsonElement arguments)
    {
        var toolName = GetPromptArgument(arguments, "tool", required: true)
            ?? throw new InvalidOperationException("Missing required prompt argument: tool");
        var task = GetPromptArgument(arguments, "task", required: false);
        var toolResourceUri = $"{CatalogToolUriPrefix}{Uri.EscapeDataString(toolName)}";

        EnsureResourceExists(toolResourceUri);

        var instruction = string.IsNullOrWhiteSpace(task)
            ? $"Review the embedded safe-call playbook and tool summary for '{toolName}'. Identify the required arguments, any likely ambiguities, any follow-up discovery steps still needed, and a safe execution plan before calling the tool."
            : $"A user wants to: {task}\n\nReview the embedded safe-call playbook and tool summary for '{toolName}'. Identify the required arguments, any likely ambiguities, whether additional discovery is still needed, and a safe execution plan before calling the tool.";

        return new
        {
            description = $"Prepare a safe execution plan for '{toolName}' using the advanced resource packs.",
            messages = BuildPromptMessages(
                instruction,
                $"{DiscoveryPackUriPrefix}safe-tool-call",
                toolResourceUri)
        };
    }

    private object[] BuildPromptMessages(string instruction, params string[] resourceUris)
    {
        var messages = new List<object>
        {
            new
            {
                role = "user",
                content = new
                {
                    type = "text",
                    text = instruction
                }
            }
        };

        foreach (var resourceUri in resourceUris)
        {
            var resourceText = TryBuildResourceContent(resourceUri);
            if (resourceText is null)
            {
                throw new InvalidOperationException($"Unknown resource: {resourceUri}");
            }

            messages.Add(new
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
            });
        }

        return messages.ToArray();
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

    private static string[] GetSchemaStringArray(JsonElement schema, string propertyName)
    {
        if (!schema.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
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

    private static string[] GetSchemaPropertyNames(JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return property.EnumerateObject()
            .Select(entry => entry.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddIfNotBlank(IDictionary<string, object?> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value;
        }
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

    private sealed record ResourcePackDefinition(
        string Name,
        string Title,
        string Description);

    private sealed record ResourcePackStep(
        int Order,
        string Action,
        string Method,
        string? Target,
        string? Prompt,
        string Purpose);

    private const string CatalogOverviewUri = "graphql-mcp://catalog/overview";
    private const string AuthorizationMetadataUri = "graphql-mcp://auth/metadata";
    private const string AuthorizationMetadataWellKnownPath = ".well-known/oauth-authorization-server";
    private const string CatalogDomainUriPrefix = "graphql-mcp://catalog/domain/";
    private const string CatalogToolUriPrefix = "graphql-mcp://catalog/tool/";
    private const string DiscoveryPackUriPrefix = "graphql-mcp://packs/discovery/";

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
