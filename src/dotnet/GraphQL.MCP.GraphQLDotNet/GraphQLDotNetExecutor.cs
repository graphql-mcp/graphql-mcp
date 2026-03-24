using System.Text.Json;
using GraphQL.MCP.Abstractions;
using GraphQL.SystemTextJson;
using Microsoft.Extensions.Logging;

namespace GraphQL.MCP.GraphQLDotNet;

/// <summary>
/// Executes GraphQL operations against a graphql-dotnet DocumentExecuter.
/// </summary>
public sealed class GraphQLDotNetExecutor : IGraphQLExecutor
{
    private readonly IDocumentExecuter _documentExecuter;
    private readonly GraphQL.Types.ISchema _schema;
    private readonly ILogger<GraphQLDotNetExecutor> _logger;

    public GraphQLDotNetExecutor(
        IDocumentExecuter documentExecuter,
        GraphQL.Types.ISchema schema,
        ILogger<GraphQLDotNetExecutor> logger)
    {
        _documentExecuter = documentExecuter;
        _schema = schema;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GraphQLExecutionResult> ExecuteAsync(
        GraphQLExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing graphql-dotnet query: {Query}", request.Query);

        var executionOptions = new ExecutionOptions
        {
            Schema = _schema,
            Query = request.Query,
            OperationName = request.OperationName,
            CancellationToken = cancellationToken,
        };

        if (request.Variables is not null)
        {
            executionOptions.Variables = new Inputs(
                request.Variables.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value));
        }

        // Forward auth/custom headers via UserContext so middleware/resolvers can access them
        if (request.Headers is { Count: > 0 })
        {
            executionOptions.UserContext = request.Headers
                .ToDictionary(
                    h => $"Header:{h.Key}",
                    h => (object?)h.Value);
        }

        var result = await _documentExecuter.ExecuteAsync(executionOptions);

        return MapResult(result);
    }

    private GraphQLExecutionResult MapResult(GraphQL.ExecutionResult executionResult)
    {
        object? data = null;
        List<Abstractions.GraphQLError>? errors = null;

        if (executionResult.Data is not null)
        {
            // Serialize graphql-dotnet's data to JSON and back to get a clean object graph
            var serializer = new GraphQLSerializer();
            using var stream = new MemoryStream();
            System.Threading.Tasks.Task.Run(async () =>
            {
                await serializer.WriteAsync(stream, executionResult);
            }).GetAwaiter().GetResult();
            stream.Position = 0;

            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                data = JsonSerializer.Deserialize<object>(dataElement.GetRawText());
            }
        }

        if (executionResult.Errors is { Count: > 0 })
        {
            errors = executionResult.Errors.Select(e => new Abstractions.GraphQLError
            {
                Message = e.Message,
                Path = e.Path?.Select(p => p?.ToString()).ToList()!
            }).ToList();
        }

        return new GraphQLExecutionResult
        {
            Data = data,
            Errors = errors
        };
    }
}
