using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GraphQL.MCP.AspNetCore;

/// <summary>
/// Extension methods for mapping the MCP endpoint in ASP.NET Core.
/// </summary>
public static class McpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the MCP Streamable HTTP endpoint.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="path">The endpoint path. Default: "/mcp".</param>
    /// <returns>The endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapGraphQLMcp(
        this IEndpointRouteBuilder endpoints,
        string path = "/mcp")
    {
        var transport = endpoints.ServiceProvider.GetRequiredService<StreamableHttpTransport>();
        var normalizedPath = path.EndsWith("/", StringComparison.Ordinal) && path.Length > 1
            ? path.TrimEnd('/')
            : path;

        endpoints.MapGet($"{normalizedPath}/.well-known/oauth-authorization-server", async context =>
        {
            await transport.HandleOAuthAuthorizationServerMetadataAsync(context);
        });

        return endpoints.MapPost(normalizedPath, async context =>
        {
            await transport.HandleRequestAsync(context);
        });
    }
}
