using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Policy;
using GraphQL.MCP.Core.Canonical;
using GraphQL.MCP.Core.Execution;
using GraphQL.MCP.Core.Publishing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GraphQL.MCP.AspNetCore;

/// <summary>
/// Initializes MCP tools asynchronously at application startup.
/// Discovers GraphQL operations, applies policy, publishes tools, and registers them.
/// </summary>
internal sealed class McpToolInitializationService : IHostedService
{
    private readonly SchemaCanonicalizer _canonicalizer;
    private readonly IMcpPolicy _policy;
    private readonly ToolPublisher _publisher;
    private readonly ToolExecutor _executor;
    private readonly McpToolRegistry _toolRegistry;
    private readonly ILogger<McpToolInitializationService> _logger;

    public McpToolInitializationService(
        SchemaCanonicalizer canonicalizer,
        IMcpPolicy policy,
        ToolPublisher publisher,
        ToolExecutor executor,
        McpToolRegistry toolRegistry,
        ILogger<McpToolInitializationService> logger)
    {
        _canonicalizer = canonicalizer;
        _policy = policy;
        _publisher = publisher;
        _executor = executor;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing MCP tools...");

        var result = await _canonicalizer.CanonicalizeAsync(cancellationToken);
        var allOps = result.Queries.Concat(result.Mutations).ToList();
        var filteredOps = _policy.Apply(allOps);
        var tools = _publisher.Publish(filteredOps);

        _executor.RegisterTools(tools);
        _toolRegistry.SetTools(tools);

        _logger.LogInformation("MCP tool initialization complete: {Count} tools published", tools.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
