# Policies

graphql-mcp keeps generated MCP tools understandable and safe by applying policy before publishing.

## Current Policy Surface

### Publication controls

- `AllowMutations`: mutations are opt-in
- `IncludedFields`: allowlist of root fields to publish
- `ExcludedFields`: denylist of root and nested fields, with glob support
- `ExcludedTypes`: skip operations that return or accept sensitive types
- `RequireDescriptionsForPublishedTools` / `graphql.mcp.require-descriptions`: only publish documented operations
- `MaxToolCount`: cap the total published tool set
- `MaxArgumentCount`: skip operations with too many inputs

### Naming controls

- `ToolPrefix`: namespace tool names per API or tenant
- `NamingPolicy`: `VerbNoun`, `Raw`, or `PrefixedRaw` on .NET; `verb-noun` or `raw` on Java

### Query shaping

- `MaxOutputDepth`: cap auto-generated selection-set depth
- nested field exclusions apply inside generated selection sets, not just at the root

### Auth controls

- auth passthrough is opt-in
- current mode forwards the incoming `Authorization` header into GraphQL execution

## Discovery Metadata

Current releases attach lightweight discovery metadata to published tools:

- category
- tags

Today this metadata is intentionally simple. It improves grouping and future discovery work without overcommitting to a custom protocol shape.

## Still Planned

- max argument complexity beyond raw argument count
- operation grouping by domain
- richer semantic hints
- description quality gates beyond presence or absence
