using GraphQL.MCP.Abstractions;
using GraphQL.MCP.Abstractions.Policy;
using GraphQL.MCP.Core.Canonical;
using GraphQL.MCP.Core.Execution;
using GraphQL.MCP.Core.Observability;
using GraphQL.MCP.Core.Policy;
using GraphQL.MCP.Core.Publishing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GraphQL.MCP.AspNetCore;

/// <summary>
/// Extension methods for registering graphql-mcp services.
/// </summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>
    /// Adds graphql-mcp services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGraphQLMcp(
        this IServiceCollection services,
        Action<McpOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.TryAddSingleton(Options.Create(new McpOptions()));
        }

        // Core services
        services.TryAddSingleton<IMcpPolicy, PolicyEngine>();
        services.TryAddSingleton<SchemaCanonicalizer>();
        services.TryAddSingleton<ToolPublisher>();
        services.TryAddSingleton<ToolExecutor>();

        // Build and register the tool list + transport on first resolution
        services.TryAddSingleton<IReadOnlyList<McpToolDescriptor>>(sp =>
        {
            var canonicalizer = sp.GetRequiredService<SchemaCanonicalizer>();
            var policy = sp.GetRequiredService<IMcpPolicy>() as PolicyEngine
                ?? throw new InvalidOperationException("PolicyEngine not registered");
            var publisher = sp.GetRequiredService<ToolPublisher>();
            var executor = sp.GetRequiredService<ToolExecutor>();
            var logger = sp.GetRequiredService<ILogger<StreamableHttpTransport>>();

            // Synchronous bootstrap — acceptable at startup
            var result = canonicalizer.CanonicalizeAsync().GetAwaiter().GetResult();
            var allOps = result.Queries.Concat(result.Mutations).ToList();
            var filteredOps = policy.Apply(allOps);
            var tools = publisher.Publish(filteredOps);

            executor.RegisterTools(tools);
            McpActivitySource.PublishedToolCount.Add(tools.Count);

            return tools;
        });

        services.TryAddSingleton<StreamableHttpTransport>();

        return services;
    }

    /// <summary>
    /// Maps the graphql-mcp endpoint using default path "/mcp".
    /// Shorthand for AddGraphQLMcp() + MapGraphQLMcp().
    /// </summary>
    public static IEndpointConventionBuilder UseGraphQLMcp(
        this IEndpointRouteBuilder endpoints,
        string path = "/mcp")
    {
        return endpoints.MapGraphQLMcp(path);
    }
}
