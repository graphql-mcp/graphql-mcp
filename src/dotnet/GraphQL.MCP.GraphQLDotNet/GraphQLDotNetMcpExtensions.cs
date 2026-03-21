using GraphQL.MCP.Abstractions;
using GraphQL.MCP.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GraphQL.MCP.GraphQLDotNet;

/// <summary>
/// Extension methods for registering graphql-dotnet MCP services.
/// </summary>
public static class GraphQLDotNetMcpExtensions
{
    /// <summary>
    /// Registers graphql-dotnet MCP adapter services.
    /// Requires <see cref="GraphQL.Types.ISchema"/> and <see cref="GraphQL.IDocumentExecuter"/>
    /// to be registered in the DI container (typically via <c>services.AddGraphQL()</c>).
    /// </summary>
    public static IServiceCollection AddGraphQLDotNetMcp(
        this IServiceCollection services,
        Action<McpOptions>? configure = null)
    {
        services.TryAddSingleton<IGraphQLSchemaSource, GraphQLDotNetSchemaSource>();
        services.TryAddSingleton<IGraphQLExecutor, GraphQLDotNetExecutor>();
        services.AddGraphQLMcp(configure);

        return services;
    }
}
