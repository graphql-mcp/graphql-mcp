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
| `prompts/list` | List available prompt templates |
| `prompts/get` | Fetch a prompt message sequence by name |
| `resources/list` | List stable catalog, tool, and discovery pack resources |
| `resources/read` | Read a catalog overview, auth summary, domain summary, tool summary, or discovery pack |
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
      "prompts": {
        "listChanged": true
      },
      "resources": {
        "listChanged": true,
        "read": true
      },
      "catalog": {
        "list": true,
        "search": true
      },
      "authorization": {
        "mode": "passthrough",
        "requiredScopes": ["orders.read"],
        "oauth2": {
          "metadata": true,
          "resource": "graphql-mcp://auth/metadata",
          "wellKnownPath": ".well-known/oauth-authorization-server"
        }
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

### Resources List Request

```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "resources/list"
}
```

### Resources Read Request

```json
{
  "jsonrpc": "2.0",
  "id": 6,
  "method": "resources/read",
  "params": {
    "uri": "graphql-mcp://catalog/overview"
  }
}
```

Common resource URIs now include:

- `graphql-mcp://catalog/overview`
- `graphql-mcp://auth/metadata`
- `graphql-mcp://catalog/domain/<domain>`
- `graphql-mcp://catalog/tool/<tool>`
- `graphql-mcp://packs/discovery/start-here`
- `graphql-mcp://packs/discovery/investigate-domain`
- `graphql-mcp://packs/discovery/safe-tool-call`

When passthrough auth metadata is configured, the transport also exposes:

- `GET /mcp/.well-known/oauth-authorization-server`
- the same relative path under a custom MCP endpoint, for example `/api/mcp/.well-known/oauth-authorization-server`

### Prompts List Request

```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "method": "prompts/list"
}
```

### Prompts Get Request

```json
{
  "jsonrpc": "2.0",
  "id": 8,
  "method": "prompts/get",
  "params": {
    "name": "explore_domain",
    "arguments": {
      "domain": "book"
    }
  }
}
```

Advanced prompt names now include:

- `plan_task_workflow`
- `compare_tools_for_task`
- `prepare_tool_call`

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

## stdio

graphql-mcp also supports MCP over `stdin`/`stdout` for local or embedded client integrations.

### Configuration

Use the same server surface, but switch the transport mode:

```csharp
builder.Services.AddHotChocolateMcp(options =>
{
    options.Transport = McpTransport.Stdio;
});
```

```yaml
graphql:
  mcp:
    transport: stdio
```

### Behavior

- one JSON-RPC request per line on standard input
- one JSON-RPC response per line on standard output
- the stdio loop preserves the negotiated MCP session across requests after `initialize`
- the same request methods are available as Streamable HTTP: `initialize`, `tools/list`, `prompts/list`, `prompts/get`, `resources/list`, `resources/read`, `catalog/list`, `catalog/search`, `tools/call`, and `ping`

### Example

Input:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
```

Output:

```json
{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","capabilities":{"tools":{"listChanged":true}}}}
{"jsonrpc":"2.0","id":2,"result":{"tools":[...]}}
```

## Future Transports

### SSE (not planned)
Server-Sent Events transport is deprecated in the MCP spec. Streamable HTTP supersedes it.
