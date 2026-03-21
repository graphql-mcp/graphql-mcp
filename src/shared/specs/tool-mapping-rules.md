# Tool Mapping Rules

Cross-ecosystem specification for how GraphQL operations map to MCP tools.

## Scope

This document defines the canonical mapping contract that both .NET and Java implementations must follow. Any divergence is a bug.

## Operation Discovery

1. Introspect the GraphQL schema to discover all root-level fields on `Query` and `Mutation` types.
2. Each root-level field becomes one candidate MCP tool.
3. Nested fields are NOT mapped to individual tools — they become part of the tool's return shape.

## Naming Rules

### VerbNoun Policy (default)
- Query fields: `get_{fieldName}` → e.g., `getUsers`, `getOrderById`
- Mutation fields: use the field name as-is (mutations are typically already verb-phrased) → e.g., `createUser`, `deleteOrder`
- If a `toolPrefix` is configured, prepend it: `myapi_getUsers`

### Raw Policy
- Use the GraphQL field name as-is.
- Prefix with `toolPrefix` if configured.

### Name Sanitization
- Replace non-alphanumeric characters (except `_`) with `_`
- Collapse consecutive underscores
- Ensure names are ≤ 64 characters (truncate with hash suffix if needed)
- Names must be unique across all published tools

## Argument Mapping

1. Each GraphQL argument becomes a property in the tool's `inputSchema` (JSON Schema).
2. GraphQL `NonNull` arguments → `required` in JSON Schema.
3. GraphQL `String` → `"type": "string"`
4. GraphQL `Int` → `"type": "integer"`
5. GraphQL `Float` → `"type": "number"`
6. GraphQL `Boolean` → `"type": "boolean"`
7. GraphQL `ID` → `"type": "string"`
8. GraphQL `Enum` → `"type": "string", "enum": [...]`
9. GraphQL `InputObject` → `"type": "object"` with nested properties
10. GraphQL `List` → `"type": "array", "items": {...}`
11. Default values should be included in `"default"` when present.
12. Descriptions from GraphQL schema should be included in `"description"`.

## Return Type Handling

1. The tool executor constructs a GraphQL selection set based on the return type.
2. Selection set depth is limited by `MaxOutputDepth` (default: 3).
3. Scalar fields at any level within depth limit are always included.
4. Object fields are expanded recursively up to the depth limit.
5. List fields are included (the list itself counts as one depth level).
6. Circular references: stop expanding when a type appears in its own ancestor chain.
7. Union/Interface types: include `__typename` and all possible type fields up to depth limit using inline fragments.

## Exclusion Rules

1. Fields listed in `ExcludedFields` are never published as tools.
2. Types listed in `ExcludedTypes` cause any operation returning/accepting that type to be excluded.
3. Mutations are excluded by default unless `AllowMutations = true`.
4. Introspection fields (`__schema`, `__type`, `__typename` at root) are never published.
5. Fields starting with `__` (double underscore) are never published.

## Tool Count Limits

1. Default maximum: 50 tools.
2. If exceeded, emit a warning log and publish only the first N (alphabetically by tool name).
3. Configurable via `MaxToolCount`.
