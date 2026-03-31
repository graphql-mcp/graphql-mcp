using System.Text;
using GraphQL.MCP.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GraphQL.MCP.AspNetCore;

/// <summary>
/// Runs graphql-mcp over stdio for local MCP clients.
/// </summary>
public sealed class StdioMcpHostedService : BackgroundService
{
    private readonly StreamableHttpTransport _transport;
    private readonly McpOptions _options;
    private readonly ILogger<StdioMcpHostedService> _logger;

    public StdioMcpHostedService(
        StreamableHttpTransport transport,
        IOptions<McpOptions> options,
        ILogger<StdioMcpHostedService> logger)
    {
        _transport = transport;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Transport != McpTransport.Stdio)
        {
            return;
        }

        _logger.LogInformation("Starting graphql-mcp stdio transport loop");
        await RunAsync(Console.In, Console.Out, stoppingToken);
    }

    public async Task RunAsync(
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        string? sessionId = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var result = await _transport.HandleStdioMessageAsync(line, sessionId, cancellationToken);
            sessionId = result.SessionId ?? sessionId;

            await output.WriteLineAsync(result.ResponseJson);
            await output.FlushAsync(cancellationToken);
        }
    }
}

