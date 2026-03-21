using System.Text.Json;
using GraphQL.MCP.Abstractions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.Logging;

namespace GraphQL.MCP.HotChocolate;

/// <summary>
/// Executes GraphQL operations against a Hot Chocolate request executor.
/// </summary>
public sealed class HotChocolateExecutor : IGraphQLExecutor
{
    private readonly IRequestExecutorResolver _executorResolver;
    private readonly ILogger<HotChocolateExecutor> _logger;

    public HotChocolateExecutor(
        IRequestExecutorResolver executorResolver,
        ILogger<HotChocolateExecutor> logger)
    {
        _executorResolver = executorResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GraphQLExecutionResult> ExecuteAsync(
        GraphQLExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var executor = await _executorResolver.GetRequestExecutorAsync(cancellationToken: cancellationToken);

        var queryRequestBuilder = OperationRequestBuilder.New()
            .SetDocument(request.Query);

        if (request.Variables is not null)
        {
            queryRequestBuilder.SetVariableValues(
                request.Variables.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value));
        }

        if (request.OperationName is not null)
        {
            queryRequestBuilder.SetOperationName(request.OperationName);
        }

        var queryRequest = queryRequestBuilder.Build();

        _logger.LogDebug("Executing Hot Chocolate query: {Query}", request.Query);

        var result = await executor.ExecuteAsync(queryRequest, cancellationToken);

        if (result is IOperationResult operationResult)
        {
            return MapResult(operationResult);
        }

        _logger.LogWarning("Unexpected result type: {Type}", result.GetType().Name);
        return new GraphQLExecutionResult
        {
            Errors = [new GraphQLError { Message = "Unexpected execution result type." }]
        };
    }

    private GraphQLExecutionResult MapResult(IOperationResult operationResult)
    {
        object? data = null;
        List<GraphQLError>? errors = null;

        if (operationResult.Data is not null)
        {
            // Serialize HC's data to JSON and back to get a clean object graph
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteResultData(writer, operationResult.Data);
            }
            stream.Position = 0;
            data = JsonSerializer.Deserialize<object>(stream);
        }

        if (operationResult.Errors is { Count: > 0 })
        {
            errors = operationResult.Errors.Select(e => new GraphQLError
            {
                Message = e.Message,
                Path = e.Path?.ToList()
            }).ToList();
        }

        return new GraphQLExecutionResult
        {
            Data = data,
            Errors = errors
        };
    }

    private static void WriteResultData(Utf8JsonWriter writer, IReadOnlyDictionary<string, object?> data)
    {
        writer.WriteStartObject();
        foreach (var kvp in data)
        {
            writer.WritePropertyName(kvp.Key);
            WriteValue(writer, kvp.Value);
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal dec:
                writer.WriteNumberValue(dec);
                break;
            case IReadOnlyDictionary<string, object?> dict:
                WriteResultData(writer, dict);
                break;
            case IReadOnlyList<object?> list:
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    WriteValue(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
