# Getting Started (.NET)

## Prerequisites

- .NET 10 SDK
- A Hot Chocolate GraphQL API (or graphql-dotnet, coming in v0.2)

## Install

```bash
dotnet add package GraphQL.MCP.HotChocolate
```

## Minimal Setup

```csharp
using GraphQL.MCP.HotChocolate;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>();

// Add MCP — one line
builder.Services.AddHotChocolateMcp();

var app = builder.Build();

// Maps /graphql (Hot Chocolate) + /mcp (MCP endpoint)
app.UseGraphQLMcp();

app.Run();

public class Query
{
    public string Hello(string name = "World") => $"Hello, {name}!";
    public List<Book> GetBooks() => [...];
}
```

That's it. Your GraphQL API now speaks MCP.

## What Happens

1. graphql-mcp introspects your Hot Chocolate schema at startup
2. Each query field becomes an MCP tool (mutations are off by default)
3. AI clients connect to `/mcp` and discover your tools
4. When a tool is called, graphql-mcp executes the corresponding GraphQL query

## Test It

Start your app:
```bash
dotnet run
```

Test the MCP endpoint:
```bash
# Initialize
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize"}'

# List tools
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'

# Call a tool
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_hello","arguments":{"name":"Claude"}}}'
```

## Connect AI Clients

### Claude Desktop

Add to your Claude Desktop config (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "my-graphql-api": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://localhost:5000/mcp"]
    }
  }
}
```

### VS Code (Copilot / Cursor)

Add to your workspace `.vscode/mcp.json`:

```json
{
  "servers": {
    "my-graphql-api": {
      "type": "http",
      "url": "http://localhost:5000/mcp"
    }
  }
}
```

## Next Steps

- [Configuration](configuration.md) — full options reference
- [Security](../security.md) — safety controls and auth
- [Mapping](../mapping.md) — how operations become tools
- [Observability](../observability.md) — logging and tracing
