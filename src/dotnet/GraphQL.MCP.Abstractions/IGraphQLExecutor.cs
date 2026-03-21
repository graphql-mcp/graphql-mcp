namespace GraphQL.MCP.Abstractions;

/// <summary>
/// Executes GraphQL operations against the underlying GraphQL server.
/// Implemented by each framework adapter.
/// </summary>
public interface IGraphQLExecutor
{
    /// <summary>
    /// Executes a GraphQL query or mutation.
    /// </summary>
    Task<GraphQLExecutionResult> ExecuteAsync(
        GraphQLExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A GraphQL execution request.
/// </summary>
public sealed class GraphQLExecutionRequest
{
    /// <summary>
    /// The GraphQL query string.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Variables for the query.
    /// </summary>
    public IDictionary<string, object?>? Variables { get; init; }

    /// <summary>
    /// Optional operation name for multi-operation documents.
    /// </summary>
    public string? OperationName { get; init; }

    /// <summary>
    /// HTTP headers to forward (e.g., Authorization).
    /// </summary>
    public IDictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// Result of a GraphQL execution.
/// </summary>
public sealed class GraphQLExecutionResult
{
    /// <summary>
    /// The data payload (typically a dictionary).
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    /// GraphQL errors, if any.
    /// </summary>
    public IReadOnlyList<GraphQLError>? Errors { get; init; }

    /// <summary>
    /// Whether the execution succeeded without errors.
    /// </summary>
    public bool IsSuccess => Errors is null || Errors.Count == 0;
}

/// <summary>
/// A GraphQL error.
/// </summary>
public sealed class GraphQLError
{
    public required string Message { get; init; }
    public IReadOnlyList<object>? Path { get; init; }
    public IDictionary<string, object>? Extensions { get; init; }
}
