using System.Diagnostics;
using FluentAssertions;
using GraphQL.MCP.Core.Observability;
using Xunit;

namespace GraphQL.MCP.Tests.Observability;

public class McpActivitySourceTests
{
    [Fact]
    public void ActivitySource_should_have_correct_name()
    {
        McpActivitySource.Source.Name.Should().Be("GraphQL.MCP");
        McpActivitySource.Source.Version.Should().Be("0.1.0");
    }

    [Fact]
    public void Meter_should_have_correct_name()
    {
        McpActivitySource.MeterName.Should().Be("GraphQL.MCP");
    }

    [Fact]
    public void ToolInvocations_counter_should_be_initialized()
    {
        McpActivitySource.ToolInvocations.Should().NotBeNull();
        McpActivitySource.ToolInvocations.Name.Should().Be("mcp.tool.invocations");
    }

    [Fact]
    public void ToolErrors_counter_should_be_initialized()
    {
        McpActivitySource.ToolErrors.Should().NotBeNull();
        McpActivitySource.ToolErrors.Name.Should().Be("mcp.tool.errors");
    }

    [Fact]
    public void ToolDuration_histogram_should_be_initialized()
    {
        McpActivitySource.ToolDuration.Should().NotBeNull();
        McpActivitySource.ToolDuration.Name.Should().Be("mcp.tool.duration");
    }

    [Fact]
    public void PublishedToolCount_counter_should_be_initialized()
    {
        McpActivitySource.PublishedToolCount.Should().NotBeNull();
        McpActivitySource.PublishedToolCount.Name.Should().Be("mcp.tools.published");
    }

    [Fact]
    public void Should_create_activities_when_listener_is_attached()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "GraphQL.MCP",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = McpActivitySource.Source.StartActivity("test.operation");

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("test.operation");
    }
}
