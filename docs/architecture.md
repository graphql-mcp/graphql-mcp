# Architecture

## Overview

graphql-mcp bridges GraphQL APIs and AI clients through the Model Context Protocol (MCP). It introspects a GraphQL schema, transforms operations into MCP tool descriptors, and executes GraphQL queries when AI clients invoke tools.

## Pipeline

```
GraphQL Schema (Hot Chocolate / graphql-dotnet / Spring GraphQL)
         │
         ▼
┌────────────────────────┐
│  IGraphQLSchemaSource  │  Framework adapter extracts schema
└──────────┬─────────────┘
           ▼
┌────────────────────────┐
│  SchemaCanonicalizer   │  Introspection → canonical operations
└──────────┬─────────────┘
           ▼
┌────────────────────────┐
│  PolicyEngine          │  Exclusions, naming, depth, mutation safety
└──────────┬─────────────┘
           ▼
┌────────────────────────┐
│  ToolPublisher         │  Canonical → MCP tool descriptors + GraphQL queries
└──────────┬─────────────┘
           ▼
┌────────────────────────┐
│  StreamableHttpTransport│  Hosts MCP endpoint, handles JSON-RPC
└──────────┬─────────────┘
           ▼
┌────────────────────────┐
│  ToolExecutor          │  MCP call → GraphQL execution → result
└────────────────────────┘
```

## Project Structure

### Abstractions (`GraphQL.MCP.Abstractions`)
Zero-dependency contracts and models:
- `IGraphQLSchemaSource` — framework adapters implement this
- `IGraphQLExecutor` — framework adapters implement this
- `McpOptions` — configuration
- `McpToolDescriptor` — tool descriptor model
- Canonical models (`CanonicalOperation`, `CanonicalType`, etc.)
- `IMcpPolicy` — policy contract

### Core (`GraphQL.MCP.Core`)
Framework-agnostic engine:
- `SchemaCanonicalizer` — normalizes schema from any source
- `PolicyEngine` — evaluates exclusion/naming/depth rules
- `ToolPublisher` — generates MCP tool descriptors and GraphQL queries
- `ToolExecutor` — translates MCP calls to GraphQL execution
- `McpActivitySource` — OpenTelemetry instrumentation

### AspNetCore (`GraphQL.MCP.AspNetCore`)
HTTP hosting layer:
- `StreamableHttpTransport` — Streamable HTTP MCP endpoint
- `McpEndpointRouteBuilderExtensions` — `MapGraphQLMcp()`
- `McpServiceCollectionExtensions` — `AddGraphQLMcp()`

### Framework Adapters
Each adapter provides `IGraphQLSchemaSource` + `IGraphQLExecutor`:
- `GraphQL.MCP.HotChocolate` — Hot Chocolate adapter
- `GraphQL.MCP.GraphQLDotNet` — graphql-dotnet adapter (v0.2)

## Design Principles

1. **Framework-agnostic core** — The core engine has zero knowledge of Hot Chocolate, graphql-dotnet, or Spring. Adapters bridge the gap.

2. **Safe by default** — Mutations are off, depth is limited, tool count is capped.

3. **Observable** — Every decision (include/exclude/name transform) is logged. OpenTelemetry spans cover the full pipeline.

4. **One-liner DX** — The minimal API is `app.UseGraphQLMcp()`. Everything else is optional configuration.

5. **Spec-aligned** — Follows MCP specification 2025-06-18 for transport and capability negotiation.

## Data Flow: Tool Invocation

```
AI Client (Claude, Copilot, Cursor)
    │
    │  POST /mcp  { "method": "tools/call", "params": { "name": "get_users" } }
    │
    ▼
StreamableHttpTransport
    │  Parse JSON-RPC → extract tool name + arguments
    ▼
ToolExecutor
    │  Look up McpToolDescriptor → build GraphQLExecutionRequest
    ▼
IGraphQLExecutor (HotChocolateExecutor)
    │  Execute GraphQL query against the schema
    ▼
GraphQLExecutionResult
    │  Serialize data → MCP response content
    ▼
StreamableHttpTransport
    │  JSON-RPC response → HTTP response
    ▼
AI Client receives structured data
```
