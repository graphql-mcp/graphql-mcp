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
| `catalog/list` | Return grouped discovery metadata for published tools |
| `capabilities/catalog` | Alias for `catalog/list` |
| `catalog/search` | Return ranked discovery matches with optional filters |
| `capabilities/search` | Alias for `catalog/search` |
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
      },
      "catalog": {
        "list": true,
        "search": true
      }
    },
    "serverInfo": {
      "name": "graphql-mcp",
      "version": "0.1.0-alpha"
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

### Catalog Response

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "domainCount": 2,
    "toolCount": 4,
    "domains": [
        {
          "domain": "book",
          "categories": ["Book"],
          "tags": ["book", "query"],
          "semanticHints": {
            "intents": ["retrieve"],
            "keywords": ["book", "query"]
          },
          "toolCount": 2,
          "toolNames": ["get_book", "list_books"]
        }
      ]
    }
  }
  ```

`tools/list` and `catalog/list` tool entries also include additive semantic hints:

```json
{
  "intent": "retrieve",
  "keywords": ["book", "id", "query"]
}
```

For a full sample session that stitches together `initialize`, `tools/list`, `catalog/list`,
`catalog/search`, and `tools/call`, see [Exploration Workflow](exploration.md).

### Catalog Search Request

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "catalog/search",
  "params": {
    "query": "book",
    "tags": ["query"],
    "limit": 5
  }
}
```

### Catalog Search Response

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "result": {
    "query": "book",
    "filters": {
      "domain": null,
      "category": null,
      "operationType": null,
      "tags": ["query"]
    },
    "totalMatches": 1,
    "domainCount": 1,
    "matches": [
      {
        "name": "get_book",
        "domain": "book",
        "tags": ["book", "query"],
        "score": 55
      }
    ]
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
