# Transports

## Streamable HTTP (default, v0.1)

graphql-mcp implements the **Streamable HTTP** transport as defined in the MCP specification (2025-06-18). This is the standard HTTP transport for MCP.

### Endpoint

- **Default path:** `/mcp`
- **Method:** `POST`
- **Content-Type:** `application/json`

### Configuration

```csharp
// Default — Streamable HTTP on /mcp
app.UseGraphQLMcp();

// Custom path
app.UseGraphQLMcp(mcpPath: "/api/mcp");
```

### Message Format

All messages use JSON-RPC 2.0:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list"
}
```

### Supported Methods

| Method | Description |
|--------|-------------|
| `initialize` | Capability negotiation |
| `tools/list` | List all available tools |
| `tools/call` | Execute a tool |
| `ping` | Health check |

### Initialize Response

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "protocolVersion": "2025-06-18",
    "capabilities": {
      "tools": {
        "listChanged": true
      }
    },
    "serverInfo": {
      "name": "graphql-mcp",
      "version": "0.1.0-alpha.1"
    }
  }
}
```

### Tool Call Request

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_users",
    "arguments": {
      "limit": 10
    }
  }
}
```

### Tool Call Response (success)

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "{\"users\":[{\"id\":1,\"name\":\"Alice\"}]}"
      }
    ]
  }
}
```

### Tool Call Response (error)

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "GraphQL execution failed: Field 'users' not found"
      }
    ],
    "isError": true
  }
}
```

### Error Responses

| Scenario | HTTP Status | JSON-RPC Error Code |
|----------|------------|-------------------|
| Invalid JSON | 400 | -32700 (Parse error) |
| Missing method | 400 | -32600 (Invalid request) |
| Unknown method | 200 | -32601 (Method not found) |
| Missing params | 200 | -32602 (Invalid params) |

## Future Transports

### stdio (planned for v0.2+)
For local/embedded scenarios where the MCP server runs as a child process. Useful for local development tools and CLI integrations.

### SSE (not planned)
Server-Sent Events transport is deprecated in the MCP spec. Streamable HTTP supersedes it.
