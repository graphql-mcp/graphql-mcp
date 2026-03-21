using GraphQL.MCP.Abstractions;
using GraphQL.MCP.AspNetCore;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GraphQL.MCP.HotChocolate;

/// <summary>
/// Extension methods for integrating graphql-mcp with Hot Chocolate.
/// </summary>
public static class HotChocolateMcpExtensions
{
    /// <summary>
    /// Adds MCP capability to a Hot Chocolate GraphQL server.
    /// Registers schema source and executor adapters for Hot Chocolate.
    /// </summary>
    /// <param name="services">The service collection (after AddGraphQL).</param>
    /// <param name="configure">Optional MCP configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHotChocolateMcp(
        this IServiceCollection services,
        Action<McpOptions>? configure = null)
    {
        // Register HC-specific adapters
        services.TryAddSingleton<IGraphQLSchemaSource, HotChocolateSchemaSource>();
        services.TryAddSingleton<IGraphQLExecutor, HotChocolateExecutor>();

        // Register core MCP services
        services.AddGraphQLMcp(configure);

        return services;
    }

    /// <summary>
    /// Adds MCP capability to the Hot Chocolate request executor builder.
    /// </summary>
    /// <param name="builder">The Hot Chocolate request executor builder.</param>
    /// <param name="configure">Optional MCP configuration.</param>
    /// <returns>The builder for chaining.</returns>
    public static IRequestExecutorBuilder AddMCP(
        this IRequestExecutorBuilder builder,
        Action<McpOptions>? configure = null)
    {
        builder.Services.AddHotChocolateMcp(configure);
        return builder;
    }

    /// <summary>
    /// One-liner: maps both the GraphQL endpoint and the MCP endpoint.
    /// Equivalent to calling MapGraphQL() + MapGraphQLMcp().
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="graphqlPath">GraphQL endpoint path. Default: "/graphql".</param>
    /// <param name="mcpPath">MCP endpoint path. Default: "/mcp".</param>
    public static void UseGraphQLMcp(
        this IEndpointRouteBuilder endpoints,
        string graphqlPath = "/graphql",
        string mcpPath = "/mcp")
    {
        endpoints.MapGraphQL(graphqlPath);
        endpoints.MapGraphQLMcp(mcpPath);
    }
}
