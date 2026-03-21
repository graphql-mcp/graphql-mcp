using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GraphQL.MCP.Core.Observability;

/// <summary>
/// OpenTelemetry instrumentation for graphql-mcp.
/// Provides Activity source for distributed tracing and Meter for metrics.
/// </summary>
public static class McpActivitySource
{
    public const string SourceName = "GraphQL.MCP";
    public const string MeterName = "GraphQL.MCP";

    /// <summary>
    /// Activity source for tracing MCP operations.
    /// </summary>
    public static readonly ActivitySource Source = new(SourceName, "0.1.0");

    /// <summary>
    /// Meter for MCP metrics.
    /// </summary>
    public static readonly Meter Meter = new(MeterName, "0.1.0");

    /// <summary>
    /// Counter for total tool invocations.
    /// </summary>
    public static readonly Counter<long> ToolInvocations =
        Meter.CreateCounter<long>("mcp.tool.invocations", "invocations", "Total MCP tool invocations");

    /// <summary>
    /// Counter for tool invocation errors.
    /// </summary>
    public static readonly Counter<long> ToolErrors =
        Meter.CreateCounter<long>("mcp.tool.errors", "errors", "Total MCP tool invocation errors");

    /// <summary>
    /// Histogram for tool execution duration.
    /// </summary>
    public static readonly Histogram<double> ToolDuration =
        Meter.CreateHistogram<double>("mcp.tool.duration", "ms", "MCP tool execution duration in milliseconds");

    /// <summary>
    /// Gauge for number of published tools.
    /// </summary>
    public static readonly UpDownCounter<int> PublishedToolCount =
        Meter.CreateUpDownCounter<int>("mcp.tools.published", "tools", "Number of currently published MCP tools");
}
