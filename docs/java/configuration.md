# Configuration (Java)

## Status

The Java configuration surface is available as an alpha-preview release on Maven Central.

## application.yml

```yaml
graphql:
  mcp:
    enabled: true
    policy-preset: curated
    policy-pack: commerce
    policy-profile:
      name: commerce-api
      included-domains:
        - book
      min-description-length: 12
      max-argument-complexity: 60
    tool-prefix: myapi
    naming-policy: verb-noun
    allow-mutations: false
    require-descriptions: false
    min-description-length: 0
    excluded-fields:
      - internalData
      - secretNote
    included-domains:
      - book
    excluded-domains:
      - admin
    max-output-depth: 3
    max-tool-count: 50
    max-argument-count: 25
    max-argument-complexity: 75
    transport: streamable-http
    authorization:
      mode: passthrough
      required-scopes:
        - orders.read
      metadata:
        issuer: https://auth.example.com
        authorization-endpoint: https://auth.example.com/authorize
        token-endpoint: https://auth.example.com/token
```

## Supported Properties

These properties are currently bound by [GraphQLMCPProperties.java](/C:/Users/inaj7/source/repos/web2/graphql-mcp/src/java/graphql-mcp-spring-boot-starter/src/main/java/dev/graphqlmcp/properties/GraphQLMCPProperties.java):

| Property | Type | Default | Description |
|--------|------|---------|-------------|
| `graphql.mcp.enabled` | `boolean` | `true` | Turns graphql-mcp auto-configuration on or off |
| `graphql.mcp.policy-preset` | `string` | `balanced` | Built-in policy baseline: `balanced`, `curated`, `strict`, `exploratory` |
| `graphql.mcp.policy-pack` | `string` | `none` | Shared profile pack: `commerce`, `content`, `operations` |
| `graphql.mcp.policy-profile.*` | object | empty | Reusable override layer applied on top of the selected preset |
| `graphql.mcp.tool-prefix` | `string` | `null` | Prefix for published MCP tool names |
| `graphql.mcp.naming-policy` | `string` | `verb-noun` | Naming style for generated tools |
| `graphql.mcp.allow-mutations` | `boolean` | `false` | Publishes mutation tools when enabled |
| `graphql.mcp.require-descriptions` | `boolean` | `false` | Skips operations that do not have descriptions |
| `graphql.mcp.min-description-length` | `int` | `0` | Skips operations with descriptions shorter than this threshold |
| `graphql.mcp.excluded-fields` | `list<string>` | `[]` | Field names to exclude from publication and selection sets |
| `graphql.mcp.included-domains` | `list<string>` | `[]` | Only publish tools from these inferred domains when non-empty |
| `graphql.mcp.excluded-domains` | `list<string>` | `[]` | Skip tools from these inferred domains |
| `graphql.mcp.max-output-depth` | `int` | `3` | Max nested selection depth in generated GraphQL queries |
| `graphql.mcp.max-tool-count` | `int` | `50` | Max number of tools published |
| `graphql.mcp.max-argument-count` | `int` | `25` | Max argument count allowed for a published tool |
| `graphql.mcp.max-argument-complexity` | `int` | `75` | Max weighted input complexity allowed for a published tool |
| `graphql.mcp.transport` | `string` | `streamable-http` | Transport mode; only Streamable HTTP is currently implemented |
| `graphql.mcp.authorization.mode` | `string` | `none` | Authorization mode; `passthrough` forwards the incoming `Authorization` header |
| `graphql.mcp.authorization.required-scopes` | `list<string>` | `[]` | Scopes advertised to authenticated MCP clients |
| `graphql.mcp.authorization.metadata.*` | object | defaults | Optional OAuth metadata surfaced through MCP resources and the well-known metadata route |

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

## Presets And Profiles

Spring configuration resolves policy in this order:

1. `graphql.mcp.policy-preset`
2. optional `graphql.mcp.policy-pack`
3. `graphql.mcp.policy-profile.*`
4. top-level `graphql.mcp.*` overrides that differ from the default baseline

This lets teams reuse a common preset, define a small profile for domain-specific curation, and still override individual limits or naming behavior in an application-local `application.yml`. Use the profile block when you need a reusable override pack, especially for values that match the normal defaults.

## What Is Not There Yet

- stdio transport
- Netflix DGS adapter
- deeper semantic ranking beyond the current lightweight hint model

## Related Docs

- [Getting Started](getting-started.md)
- [Security](../security.md)
- [Transports](../transports.md)
- [Observability](../observability.md)
