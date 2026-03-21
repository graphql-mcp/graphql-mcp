# Observability

graphql-mcp ships with built-in OpenTelemetry instrumentation for tracing, metrics, and structured logging.

## OpenTelemetry

### Activity Source

Source name: `GraphQL.MCP`

#### Spans

| Span Name | Description |
|-----------|-------------|
| `mcp.tool.execute` | Full tool execution lifecycle |

#### Span Attributes

| Attribute | Type | Description |
|-----------|------|-------------|
| `mcp.tool.name` | string | Tool name that was invoked |
| `mcp.tool.success` | bool | Whether execution succeeded |
| `graphql.operation_type` | string | "Query" or "Mutation" |
| `graphql.field` | string | Original GraphQL field name |
| `error` | bool | Set to true on failure |

### Meter

Meter name: `GraphQL.MCP`

#### Metrics

| Metric | Type | Unit | Description |
|--------|------|------|-------------|
| `mcp.tool.invocations` | Counter | invocations | Total tool calls |
| `mcp.tool.errors` | Counter | errors | Total failed tool calls |
| `mcp.tool.duration` | Histogram | ms | Execution time per call |
| `mcp.tools.published` | UpDownCounter | tools | Currently published tool count |

### Integration

Add OpenTelemetry to your host to collect traces and metrics:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("GraphQL.MCP")          // <-- add this
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("GraphQL.MCP")           // <-- add this
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());
```

## Structured Logging

graphql-mcp uses `Microsoft.Extensions.Logging` with structured log messages. Key log events:

### Schema Discovery

```
info: SchemaCanonicalizer — Starting schema canonicalization
info: SchemaCanonicalizer — Canonicalization complete: 12 queries, 4 mutations discovered
```

### Policy Decisions

```
info: PolicyEngine — Excluding mutation 'deleteUser' — AllowMutations is false
info: PolicyEngine — Excluding field 'password' — listed in ExcludedFields
info: PolicyEngine — Excluding field 'auditLogs' — return type 'AuditLog' is in ExcludedTypes
warn: PolicyEngine — Tool count 65 exceeds MaxToolCount 50 — truncating to first 50 (alphabetically)
```

### Tool Publishing

```
dbug: ToolPublisher — Published tool 'get_users' from GraphQL field 'users' (Query)
info: ToolPublisher — Published 12 MCP tools
```

### Tool Execution

```
dbug: ToolExecutor — Executing GraphQL Query for tool 'get_users': query McpOperation { users { id name } }
warn: ToolExecutor — GraphQL execution errors for tool 'get_users': Field not found
warn: ToolExecutor — Tool 'unknown_tool' not found
```

### Log Levels

| Level | What's logged |
|-------|--------------|
| `Debug` | Individual tool publish events, query strings, MCP message details |
| `Information` | Counts (tools published, operations discovered), policy exclusions |
| `Warning` | Execution errors, tool count truncation, unknown tool calls |
| `Error` | Unhandled exceptions during tool execution |

### Configuration

```json
{
  "Logging": {
    "LogLevel": {
      "GraphQL.MCP": "Information"
    }
  }
}
```

Set to `Debug` to see generated GraphQL queries and individual tool mappings.
