# graphql-mcp

> **MCP capabilities for every GraphQL server — Hot Chocolate, graphql-dotnet, Spring GraphQL, and more.**
> Cross-framework curation, policy controls, and AI-friendly capability discovery.

[![NuGet](https://img.shields.io/nuget/v/GraphQL.MCP.HotChocolate?label=HotChocolate&color=blue)](https://www.nuget.org/packages/GraphQL.MCP.HotChocolate)
[![NuGet](https://img.shields.io/nuget/v/GraphQL.MCP.GraphQLDotNet?label=GraphQLDotNet&color=blue)](https://www.nuget.org/packages/GraphQL.MCP.GraphQLDotNet)
[![Maven Central](https://img.shields.io/maven-central/v/dev.graphql-mcp/graphql-mcp-spring-boot-starter?label=Maven%20Central)](https://central.sonatype.com/artifact/dev.graphql-mcp/graphql-mcp-spring-boot-starter)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET CI](https://github.com/graphql-mcp/graphql-mcp/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/graphql-mcp/graphql-mcp/actions/workflows/dotnet-ci.yml)

<!-- TODO: Add 30-second demo GIF here -->
<!-- ![Demo](docs/assets/demo.gif) -->

---

## What is this?

graphql-mcp is a cross-framework GraphQL AI capability layer. It turns your existing GraphQL API into an [MCP server](https://modelcontextprotocol.io/) that AI clients (Claude, Copilot, Cursor, Windsurf) can use directly — with curation, policy controls, and safety built in.

No schema duplication. No sidecar process. Just your GraphQL API, now speaking MCP.

## Why graphql-mcp?

Some frameworks are adding native MCP support (e.g., Hot Chocolate 16). graphql-mcp goes further:

| | Native framework MCP | graphql-mcp |
|---|---|---|
| Cross-framework support | One framework only | Hot Chocolate, graphql-dotnet, Spring |
| Curation & policy engine | Varies | Glob-pattern field/type exclusion, mutation blocking, depth limits |
| AI-friendly naming | Varies | VerbNoun, Raw, PrefixedRaw policies with tool prefixes |
| Observability | Varies | Built-in OpenTelemetry traces + metrics |
| Portable core | No | Framework-agnostic engine, adapters are thin |

**Use native MCP when** your framework ships it and it covers your needs.
**Use graphql-mcp when** you need cross-framework support, advanced curation, or operate across multiple GraphQL backends.

## Discovery

graphql-mcp now ships two lightweight discovery surfaces:

- `tools/list` includes per-tool `domain`, `category`, `tags`, and `semanticHints` annotations
- `catalog/list` returns grouped domain summaries, semantic hints, and tool metadata for exploration UIs
- `catalog/search` returns ranked matches with optional query/domain/category/tag filters for discovery UIs

## Supported AI Clients

| Client | Status |
|--------|--------|
| Claude Desktop | Tested |
| GitHub Copilot | Compatible |
| Cursor | Compatible |
| Windsurf | Compatible |
| Any MCP-compatible client | Compatible |

## Supported GraphQL Frameworks

| Framework | Status | Package |
|-----------|--------|---------|
| Hot Chocolate (.NET) | Alpha | [`GraphQL.MCP.HotChocolate`](https://www.nuget.org/packages/GraphQL.MCP.HotChocolate) |
| graphql-dotnet (.NET) | Alpha | [`GraphQL.MCP.GraphQLDotNet`](https://www.nuget.org/packages/GraphQL.MCP.GraphQLDotNet) |
| Spring GraphQL (Java) | Alpha Preview | [`dev.graphql-mcp:graphql-mcp-spring-boot-starter`](https://central.sonatype.com/artifact/dev.graphql-mcp/graphql-mcp-spring-boot-starter) |
| Netflix DGS (Java) | Planned | - |

---

## Install

### Hot Chocolate

```bash
dotnet add package GraphQL.MCP.HotChocolate
```

### graphql-dotnet

```bash
dotnet add package GraphQL.MCP.GraphQLDotNet
```

### Java (Spring)

```xml
<dependency>
    <groupId>dev.graphql-mcp</groupId>
    <artifactId>graphql-mcp-spring-boot-starter</artifactId>
    <version>0.1.0-alpha.3</version>
</dependency>
```

---

## Quick Start

### Hot Chocolate — Zero Config

```csharp
using GraphQL.MCP.HotChocolate;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>();

builder.Services.AddHotChocolateMcp();  // one line

var app = builder.Build();
app.UseGraphQLMcp();                     // maps /graphql + /mcp
app.Run();
```

### graphql-dotnet — Zero Config

```csharp
using GraphQL.MCP.AspNetCore;
using GraphQL.MCP.GraphQLDotNet;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGraphQL(b => b
    .AddSchema<MySchema>()
    .AddSystemTextJson());

builder.Services.AddGraphQLDotNetMcp();  // one line

var app = builder.Build();
app.UseGraphQL("/graphql");
app.UseGraphQLMcp();                      // maps /mcp
app.Run();
```

### Full Config (any adapter)

```csharp
builder.Services.AddHotChocolateMcp(options =>   // or AddGraphQLDotNetMcp
{
    options.ToolPrefix = "myapi";
    options.MaxOutputDepth = 3;
    options.NamingPolicy = ToolNamingPolicy.VerbNoun;

    options.AllowMutations = false;
    options.ExcludedFields.Add("internalNotes");
    options.ExcludedTypes.Add("AdminPanel");

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
GraphQL Schema --> Schema Canonicalization --> Policy Engine --> MCP Tools
                                                                    |
AI Client --> POST /mcp --> Tool Executor --> GraphQL Execution --> Result
```

1. **Introspects** your GraphQL schema at startup
2. **Canonicalizes** operations into a framework-agnostic model
3. **Applies policies** — field/type exclusions, naming, depth limits, mutation safety
4. **Publishes tools** as MCP tool descriptors with JSON Schema inputs
5. **Executes** GraphQL queries when AI clients invoke tools

## Safety by Default

| Feature | Default |
|---------|---------|
| Mutations exposed | **No** — opt-in only |
| Max output depth | **3 levels** |
| Max tool count | **50** |
| Auth forwarded | **No** — opt-in passthrough |
| Sensitive fields | **You configure** `ExcludedFields` with glob patterns |
| Selection set exclusion | **Yes** — excluded fields filtered from nested types too |

## Observability

Built-in OpenTelemetry support:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("GraphQL.MCP"))
    .WithMetrics(m => m.AddMeter("GraphQL.MCP"));
```

Metrics: `mcp.tool.invocations`, `mcp.tool.errors`, `mcp.tool.duration`

---

## Architecture

```
+---------------------------------------------+
|  GraphQL.MCP.Abstractions                   |  Contracts (zero deps)
+---------------------------------------------+
|  GraphQL.MCP.Core                           |  Engine (canonicalize, policy, publish, execute)
+---------------------------------------------+
|  GraphQL.MCP.AspNetCore                     |  HTTP transport (Streamable HTTP)
+----------------------+----------------------+
|  GraphQL.MCP.        |  GraphQL.MCP.        |
|  HotChocolate        |  GraphQLDotNet       |  Framework adapters
+----------------------+----------------------+
```

The core engine is fully framework-agnostic. Adding a new framework adapter means implementing two interfaces: `IGraphQLSchemaSource` and `IGraphQLExecutor`.

See [docs/architecture.md](docs/architecture.md) for the full design.

---

## Claude Desktop Setup

Start your GraphQL MCP server, then add to `claude_desktop_config.json`:

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

Restart Claude Desktop. Your GraphQL operations will appear as tools.

---

## Documentation

| Topic | Link |
|-------|------|
| Getting Started (.NET) | [docs/dotnet/getting-started.md](docs/dotnet/getting-started.md) |
| Configuration (.NET) | [docs/dotnet/configuration.md](docs/dotnet/configuration.md) |
| Getting Started (Java) | [docs/java/getting-started.md](docs/java/getting-started.md) |
| Configuration (Java) | [docs/java/configuration.md](docs/java/configuration.md) |
| Discovery | [docs/discovery.md](docs/discovery.md) |
| Policies | [docs/policies.md](docs/policies.md) |
| Adapters | [docs/adapters.md](docs/adapters.md) |
| Roadmap | [docs/roadmap.md](docs/roadmap.md) |
| How Mapping Works | [docs/mapping.md](docs/mapping.md) |
| Security Model | [docs/security.md](docs/security.md) |
| Transports | [docs/transports.md](docs/transports.md) |
| Observability | [docs/observability.md](docs/observability.md) |
| Architecture | [docs/architecture.md](docs/architecture.md) |

---

## Roadmap

- [x] .NET Core engine (framework-agnostic)
- [x] Hot Chocolate adapter
- [x] graphql-dotnet adapter
- [x] Streamable HTTP transport
- [x] Policy engine (field/type exclusion with globs, naming, depth, mutation blocking)
- [x] Selection set field exclusion (nested types)
- [x] OpenTelemetry instrumentation
- [x] Spring GraphQL starter
- [ ] MCP Resources (schema summary, type docs)
- [ ] MCP Prompts (curated exploration templates)
- [x] AI-friendly discovery (domain grouping, semantic hints, and grouped catalogs)
- [ ] OAuth 2.1 metadata support
- [ ] stdio transport
- [ ] Netflix DGS adapter

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.

## License

[MIT](LICENSE) — use it however you want.
