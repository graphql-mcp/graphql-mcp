# Configuration (Java)

## Status

The Java configuration surface is available for local preview, but the Java track has not been published yet. Treat this as preview documentation for the current Spring implementation.

## application.yml

```yaml
graphql:
  mcp:
    enabled: true
    tool-prefix: myapi
    naming-policy: verb-noun
    allow-mutations: false
    excluded-fields:
      - internalData
      - secretNote
    max-output-depth: 3
    max-tool-count: 50
    transport: streamable-http
    authorization:
      mode: passthrough
```

## Supported Properties

These properties are currently bound by [GraphQLMCPProperties.java](/C:/Users/inaj7/source/repos/web2/graphql-mcp/src/java/graphql-mcp-spring-boot-starter/src/main/java/dev/graphqlmcp/properties/GraphQLMCPProperties.java):

| Property | Type | Default | Description |
|--------|------|---------|-------------|
| `graphql.mcp.enabled` | `boolean` | `true` | Turns graphql-mcp auto-configuration on or off |
| `graphql.mcp.tool-prefix` | `string` | `null` | Prefix for published MCP tool names |
| `graphql.mcp.naming-policy` | `string` | `verb-noun` | Naming style for generated tools |
| `graphql.mcp.allow-mutations` | `boolean` | `false` | Publishes mutation tools when enabled |
| `graphql.mcp.excluded-fields` | `list<string>` | `[]` | Field names to exclude from publication and selection sets |
| `graphql.mcp.max-output-depth` | `int` | `3` | Max nested selection depth in generated GraphQL queries |
| `graphql.mcp.max-tool-count` | `int` | `50` | Max number of tools published |
| `graphql.mcp.transport` | `string` | `streamable-http` | Transport mode; only Streamable HTTP is currently implemented |
| `graphql.mcp.authorization.mode` | `string` | `none` | Authorization mode; `passthrough` forwards the incoming `Authorization` header |

## Endpoint

The Java web transport serves MCP over:

```text
POST /mcp
```

The current controller also supports overriding the path with:

```properties
graphql.mcp.endpoint=/custom-mcp
```

## Naming Policies

### `verb-noun` (default)

Examples:

```text
hello       -> get_hello
book        -> get_book
createBook  -> createBook
```

### `raw`

Examples:

```text
hello       -> hello
book        -> book
createBook  -> createBook
```

## Auth Passthrough

When `graphql.mcp.authorization.mode=passthrough`, the current HTTP transport forwards the incoming `Authorization` header into GraphQL execution context so downstream resolvers can read it.

## Session Behavior

The Java MCP controller currently requires:

1. `initialize`
2. capture `Mcp-Session-Id`
3. send `Mcp-Session-Id` for `tools/list`, `tools/call`, and `ping`

Requests without a valid session header are rejected.

## What Is Not There Yet

- OAuth 2.1 metadata support
- Java resources/prompts support
- stdio transport
- advanced discovery metadata such as tags and domain grouping

## Related Docs

- [Getting Started](getting-started.md)
- [Security](../security.md)
- [Transports](../transports.md)
- [Observability](../observability.md)
