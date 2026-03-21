# graphql-mcp

> **Expose your GraphQL API as MCP capabilities — one line of code.**
> .NET 10 today, Java Spring next. No proxies. No external processes.

[![NuGet](https://img.shields.io/nuget/v/GraphQL.MCP.HotChocolate?label=NuGet&color=blue)](https://www.nuget.org/packages/GraphQL.MCP.HotChocolate)
[![Maven Central](https://img.shields.io/maven-central/v/dev.graphql-mcp/graphql-mcp-spring?label=Maven%20Central)](https://central.sonatype.com/artifact/dev.graphql-mcp/graphql-mcp-spring)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET CI](https://github.com/graphql-mcp/graphql-mcp/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/graphql-mcp/graphql-mcp/actions/workflows/dotnet-ci.yml)

<!-- TODO: Add 30-second demo GIF here -->
<!-- ![Demo](docs/assets/demo.gif) -->

---

## What is this?

graphql-mcp turns your existing GraphQL API into an [MCP server](https://modelcontextprotocol.io/) that AI clients (Claude, Copilot, Cursor, Windsurf) can use directly. No schema duplication. No sidecar process. Just your GraphQL API, now speaking MCP.

## Supported AI Clients

| Client | Status |
|--------|--------|
| Claude Desktop | ✅ |
| GitHub Copilot | ✅ |
| Cursor | ✅ |
| Windsurf | ✅ |
| Any MCP-compatible client | ✅ |

## Supported GraphQL Frameworks

| Framework | Status | Package |
|-----------|--------|---------|
| Hot Chocolate (.NET) | ✅ v0.1 | `GraphQL.MCP.HotChocolate` |
| graphql-dotnet | 🚧 v0.2 | `GraphQL.MCP.GraphQLDotNet` |
| Spring GraphQL (Java) | 🚧 v0.3 | `dev.graphql-mcp:graphql-mcp-spring` |

---

## Install

### .NET (Hot Chocolate)

```bash
dotnet add package GraphQL.MCP.HotChocolate
```

### Java (Spring) — coming soon

```xml
<dependency>
    <groupId>dev.graphql-mcp</groupId>
    <artifactId>graphql-mcp-spring-boot-starter</artifactId>
    <version>0.1.0</version>
</dependency>
```

---

## Quick Start

### .NET — Zero Config

```csharp
using GraphQL.MCP.HotChocolate;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>();

builder.Services.AddHotChocolateMcp();  // ← one line

var app = builder.Build();
app.UseGraphQLMcp();                     // ← maps /graphql + /mcp
app.Run();
```

### .NET — Full Config

```csharp
builder.Services.AddHotChocolateMcp(options =>
{
    options.ToolPrefix = "myapi";
    options.MaxOutputDepth = 3;
    options.NamingPolicy = ToolNamingPolicy.VerbNoun;

    options.AllowMutations = false;
    options.ExcludedFields.Add("internalData");

    options.Authorization.Mode = McpAuthMode.Passthrough;
    options.Transport = McpTransport.StreamableHttp;
});
```

### Java — Zero Config

```java
@EnableGraphQLMCP
@SpringBootApplication
public class App {
    public static void main(String[] args) {
        SpringApplication.run(App.class, args);
    }
}
```

### Java — Full Config (application.yml)

```yaml
graphql:
  mcp:
    enabled: true
    tool-prefix: myapi
    max-output-depth: 3
    naming-policy: verb-noun
    allow-mutations: false
    excluded-fields:
      - internalData
    authorization:
      mode: passthrough
    transport: streamable-http
```

---

## How It Works

```
GraphQL Schema ──→ Schema Canonicalization ──→ Policy Engine ──→ MCP Tools
                                                                    │
AI Client ──→ POST /mcp ──→ Tool Executor ──→ GraphQL Execution ──→ Result
```

1. **Introspects** your GraphQL schema at startup
2. **Canonicalizes** operations into a framework-agnostic model
3. **Applies policies** — exclusions, naming, depth limits, mutation safety
4. **Publishes tools** as MCP tool descriptors with JSON Schema inputs
5. **Executes** GraphQL queries when AI clients invoke tools

## Safety by Default

| Feature | Default |
|---------|---------|
| Mutations exposed | **No** — opt-in only |
| Max output depth | **3 levels** |
| Max tool count | **50** |
| Auth forwarded | **No** — opt-in passthrough |
| Sensitive fields | **You configure** `ExcludedFields` |

## Observability

Built-in OpenTelemetry support from v0.1:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("GraphQL.MCP"))
    .WithMetrics(m => m.AddMeter("GraphQL.MCP"));
```

Metrics: `mcp.tool.invocations`, `mcp.tool.errors`, `mcp.tool.duration`

---

## Architecture

```
┌─────────────────────────────────────────────┐
│  GraphQL.MCP.Abstractions                   │  ← Contracts (zero deps)
├─────────────────────────────────────────────┤
│  GraphQL.MCP.Core                           │  ← Engine (canonicalize, policy, publish, execute)
├─────────────────────────────────────────────┤
│  GraphQL.MCP.AspNetCore                     │  ← HTTP transport (Streamable HTTP)
├──────────────────────┬──────────────────────┤
│  GraphQL.MCP.        │  GraphQL.MCP.        │
│  HotChocolate        │  GraphQLDotNet       │  ← Framework adapters
└──────────────────────┴──────────────────────┘
```

See [docs/architecture.md](docs/architecture.md) for the full design.

---

## Documentation

| Topic | Link |
|-------|------|
| Getting Started (.NET) | [docs/dotnet/getting-started.md](docs/dotnet/getting-started.md) |
| Configuration | [docs/dotnet/configuration.md](docs/dotnet/configuration.md) |
| How Mapping Works | [docs/mapping.md](docs/mapping.md) |
| Security Model | [docs/security.md](docs/security.md) |
| Transports | [docs/transports.md](docs/transports.md) |
| Observability | [docs/observability.md](docs/observability.md) |
| Architecture | [docs/architecture.md](docs/architecture.md) |

---

## Roadmap

- [x] .NET Core engine
- [x] Hot Chocolate adapter
- [x] Streamable HTTP transport
- [x] Policy engine (exclusions, naming, depth)
- [x] OpenTelemetry instrumentation
- [ ] graphql-dotnet adapter
- [ ] Spring GraphQL starter
- [ ] MCP Resources (schema summary, type docs)
- [ ] MCP Prompts (curated exploration templates)
- [ ] OAuth 2.1 metadata support
- [ ] stdio transport
- [ ] Netflix DGS adapter
- [ ] MCP Registry listing

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.

## License

[MIT](LICENSE) — use it however you want.