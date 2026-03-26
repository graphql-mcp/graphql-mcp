# Transport Behavior

Cross-ecosystem specification for MCP transport in graphql-mcp.

## Supported Transports

### v0.1: Streamable HTTP (primary)
- Aligned with MCP specification 2025-06-18.
- Single HTTP endpoint handles all MCP messages.
- Supports both request-response and streaming patterns.
- Default endpoint path: `/mcp`

### Future: stdio
- For local/embedded scenarios.
- Not shipped in v0.1.

### Deprecated: SSE
- Server-Sent Events transport is NOT supported.
- The MCP spec has moved to Streamable HTTP as the standard HTTP transport.

## Streamable HTTP Behavior

### Endpoint
- Default path: `/mcp`
- Accepts `POST` requests with `Content-Type: application/json`
- Returns `Content-Type: application/json` for single responses
- Returns `Content-Type: text/event-stream` for streaming responses

### Session Management
- Each client connection gets a session ID via `Mcp-Session-Id` header.
- Sessions track tool list state for change notifications.

### Message Flow

```
Client                          Server
  |                               |
  |  POST /mcp (initialize)      |
  |------------------------------>|
  |  200 OK (capabilities)       |
  |<------------------------------|
  |                               |
  |  POST /mcp (tools/list)      |
  |------------------------------>|
  |  200 OK (tool descriptors)   |
  |<------------------------------|
  |                               |
  |  POST /mcp (tools/call)      |
  |------------------------------>|
  |  200 OK (result)             |
  |<------------------------------|
```

### Capabilities Advertised

```json
{
  "capabilities": {
    "tools": {
      "listChanged": true
    }
  },
  "serverInfo": {
    "name": "graphql-mcp",
    "version": "0.1.0-alpha.3"
  }
}
```

### Error Responses
- Invalid JSON → HTTP 400
- Unknown method → MCP error response with `MethodNotFound` code
- Tool execution failure → MCP error response with tool-specific error details
- Server error → HTTP 500 with generic error (no internals)

## Integration Points

### .NET (ASP.NET Core)
- Registered via `app.UseGraphQLMcp()` which maps the endpoint.
- Uses ASP.NET Core's request pipeline for middleware (auth, logging, etc.).
- Integrates with the MCP C# SDK for protocol handling.

### Java (Spring Boot)
- Auto-configured via Spring Boot starter.
- Registers a Spring MVC/WebFlux handler for the MCP endpoint.
- Uses Spring Security integration for auth passthrough.
