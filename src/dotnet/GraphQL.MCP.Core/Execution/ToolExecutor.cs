using System.Diagnostics;
using System.Text.Json;
using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Core.Observability;
using Microsoft.Extensions.Logging;

namespace GraphQL.MCP.Core.Execution;

/// <summary>
/// Executes MCP tool calls by translating them into GraphQL operations.
/// </summary>
public sealed class ToolExecutor
{
    private readonly IGraphQLExecutor _graphqlExecutor;
    private readonly ILogger<ToolExecutor> _logger;
    private readonly Dictionary<string, McpToolDescriptor> _toolRegistry = new(StringComparer.Ordinal);

    public ToolExecutor(
        IGraphQLExecutor graphqlExecutor,
        ILogger<ToolExecutor> logger)
    {
        _graphqlExecutor = graphqlExecutor;
        _logger = logger;
    }

    /// <summary>
    /// Registers published tools so they can be invoked by name.
    /// </summary>
    public void RegisterTools(IReadOnlyList<McpToolDescriptor> tools)
    {
        _toolRegistry.Clear();
        foreach (var tool in tools)
        {
            if (_toolRegistry.ContainsKey(tool.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate MCP tool name '{tool.Name}' cannot be registered for execution.");
            }

            _toolRegistry[tool.Name] = tool;
        }
        _logger.LogDebug("Registered {Count} tools for execution", tools.Count);
    }

    /// <summary>
    /// Executes a tool call and returns the result.
    /// </summary>
    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        JsonElement? arguments,
        IDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpActivitySource.Source.StartActivity("mcp.tool.execute");
        activity?.SetTag("mcp.tool.name", toolName);
        var stopwatch = Stopwatch.StartNew();

        McpActivitySource.ToolInvocations.Add(1, new KeyValuePair<string, object?>("tool.name", toolName));

        if (!_toolRegistry.TryGetValue(toolName, out var descriptor))
        {
            _logger.LogWarning("Tool '{ToolName}' not found", toolName);
            McpActivitySource.ToolErrors.Add(1, new KeyValuePair<string, object?>("tool.name", toolName));
            return ToolExecutionResult.Error($"Tool '{toolName}' not found.");
        }

        activity?.SetTag("graphql.operation_type", descriptor.OperationType.ToString());
        activity?.SetTag("graphql.field", descriptor.GraphQLFieldName);

        try
        {
            // Build variables from MCP arguments
            var variables = BuildVariables(arguments, descriptor);

            var request = new GraphQLExecutionRequest
            {
                Query = descriptor.GraphQLQuery,
                Variables = variables,
                Headers = headers
            };

            _logger.LogDebug(
                "Executing GraphQL {OpType} for tool '{ToolName}': {Query}",
                descriptor.OperationType, toolName, descriptor.GraphQLQuery);

            var result = await _graphqlExecutor.ExecuteAsync(request, cancellationToken);

            if (!result.IsSuccess)
            {
                var errorMessages = result.Errors!
                    .Select(e => e.Message)
                    .ToList();

                _logger.LogWarning(
                    "GraphQL execution errors for tool '{ToolName}': {Errors}",
                    toolName, string.Join("; ", errorMessages));

                McpActivitySource.ToolErrors.Add(1, new KeyValuePair<string, object?>("tool.name", toolName));
                return ToolExecutionResult.Error(
                    $"GraphQL execution failed: {string.Join("; ", errorMessages)}");
            }

            var jsonResult = JsonSerializer.Serialize(result.Data, JsonSerializerOptions);
            activity?.SetTag("mcp.tool.success", true);

            return ToolExecutionResult.Success(jsonResult);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unhandled error executing tool '{ToolName}'", toolName);
            activity?.SetTag("mcp.tool.success", false);
            activity?.SetTag("error", true);
            McpActivitySource.ToolErrors.Add(1, new KeyValuePair<string, object?>("tool.name", toolName));

            return ToolExecutionResult.Error("An internal error occurred while executing the tool.");
        }
        finally
        {
            stopwatch.Stop();
            McpActivitySource.ToolDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("tool.name", toolName));
        }
    }

    /// <summary>
    /// Checks whether a tool is registered.
    /// </summary>
    public bool HasTool(string toolName) => _toolRegistry.ContainsKey(toolName);

    /// <summary>
    /// Returns all registered tool names.
    /// </summary>
    public IReadOnlyList<string> GetToolNames() => _toolRegistry.Keys.ToList();

    private static Dictionary<string, object?>? BuildVariables(
        JsonElement? arguments,
        McpToolDescriptor descriptor)
    {
        if (arguments is null || arguments.Value.ValueKind != JsonValueKind.Object)
            return null;

        var variables = new Dictionary<string, object?>();

        foreach (var property in arguments.Value.EnumerateObject())
        {
            // Map MCP argument name to GraphQL variable name
            var graphqlVarName = descriptor.ArgumentMapping.TryGetValue(property.Name, out var mapped)
                ? mapped
                : property.Name;

            variables[graphqlVarName] = DeserializeJsonElement(property.Value);
        }

        return variables;
    }

    private static object? DeserializeJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt32(out var i) ? i
            : element.TryGetInt64(out var l) ? l
            : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => element.EnumerateArray().Select(DeserializeJsonElement).ToList(),
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => DeserializeJsonElement(p.Value)),
        _ => element.GetRawText()
    };

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

/// <summary>
/// Result of an MCP tool execution.
/// </summary>
public sealed class ToolExecutionResult
{
    public bool IsSuccess { get; private init; }
    public string? Content { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static ToolExecutionResult Success(string content) => new()
    {
        IsSuccess = true,
        Content = content
    };

    public static ToolExecutionResult Error(string message) => new()
    {
        IsSuccess = false,
        ErrorMessage = message
    };
}
