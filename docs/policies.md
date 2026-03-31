# Policies

graphql-mcp keeps generated MCP tools understandable and safe by applying policy before publishing.

## Built-In Presets

graphql-mcp now ships reusable presets for common curation modes:

- `Balanced`: the default general-purpose baseline
- `Curated`: stronger description and complexity expectations for curated catalogs
- `Strict`: smaller, more heavily documented tool surfaces for external-facing use
- `Exploratory`: broader discovery limits for internal exploration and agent-assisted investigation

Both runtimes also support a reusable profile override layer on top of a preset, so teams can define a preset once and then apply API-specific domain, description, and complexity overrides.

## Current Policy Surface

### Publication controls

- `AllowMutations`: mutations are opt-in
- `IncludedFields`: allowlist of root fields to publish
- `ExcludedFields`: denylist of root and nested fields, with glob support
- `ExcludedTypes`: skip operations that return or accept sensitive types
- `RequireDescriptionsForPublishedTools` / `graphql.mcp.require-descriptions`: only publish documented operations
- `MinDescriptionLength` / `graphql.mcp.min-description-length`: skip descriptions that are too short to be useful
- `MaxToolCount`: cap the total published tool set
- `MaxArgumentCount`: skip operations with too many inputs
- `MaxArgumentComplexity` / `graphql.mcp.max-argument-complexity`: skip operations whose nested input shape is too complex
- `IncludedDomains` / `graphql.mcp.included-domains`: allowlist inferred domains
- `ExcludedDomains` / `graphql.mcp.excluded-domains`: denylist inferred domains

### Naming controls

- `ToolPrefix`: namespace tool names per API or tenant
- `NamingPolicy`: `VerbNoun`, `Raw`, or `PrefixedRaw` on .NET; `verb-noun` or `raw` on Java
- `PolicyPreset` / `graphql.mcp.policy-preset`: choose a built-in preset baseline
- `PolicyProfile` / `graphql.mcp.policy-profile.*`: reusable per-API overrides layered on top of a preset

### Query shaping

- `MaxOutputDepth`: cap auto-generated selection-set depth
- nested field exclusions apply inside generated selection sets, not just at the root

### Auth controls

- auth passthrough is opt-in
- current mode forwards the incoming `Authorization` header into GraphQL execution

## Discovery Metadata

Current releases attach lightweight discovery metadata to published tools:

- domain
- category
- tags
- semantic hints

This metadata is intentionally lightweight enough to stay portable across runtimes while still helping clients group, search, and select tools safely.

## Still Planned

- richer semantic hints beyond the current lightweight hint model
- deeper domain inference for very large or ambiguous schemas
- higher-level shared profile packs for common schema shapes and industries
