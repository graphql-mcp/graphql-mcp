# Safety Model

Cross-ecosystem specification for safety controls in graphql-mcp.

## Principles

1. **Safe by default**: Only read operations (queries) are exposed unless explicitly opted in.
2. **Explicit opt-in for writes**: Mutations require `AllowMutations = true`.
3. **No silent data leaks**: Sensitive fields must be explicitly excluded via `ExcludedFields`.
4. **Auth passthrough**: The library never strips, injects, or modifies authentication tokens — it forwards them.
5. **Observable exclusions**: Every excluded operation/field is logged with a reason.

## Default Behavior

| Setting | Default | Rationale |
|---------|---------|-----------|
| AllowMutations | `false` | Prevents AI from modifying data by default |
| MaxOutputDepth | `3` | Prevents massive response payloads |
| MaxToolCount | `50` | Prevents tool overload in AI clients |
| ExcludedFields | `[]` | No assumptions about what's sensitive |
| Authorization.Mode | `None` | Requires explicit auth configuration |

## Mutation Safety

When `AllowMutations = true`:
- All mutation fields are published as tools.
- The tool description includes `[MUTATION]` prefix to signal write intent.
- AI clients can choose to confirm with users before executing mutations.

When `AllowMutations = false` (default):
- Mutation fields are discovered during introspection but not published.
- A structured log entry records each excluded mutation.

## Field Exclusion

Fields can be excluded by:
1. **Exact name match**: `ExcludedFields = ["password", "ssn"]`
2. **Type-level exclusion**: `ExcludedTypes = ["AuditLog"]` — excludes any operation that returns or accepts this type.

Excluded operations are logged at `Information` level with the reason.

## Depth Limiting

- `MaxOutputDepth` controls how deep the auto-generated selection set goes.
- Default is 3, meaning: `field { child { grandchild { scalar } } }`.
- Deeper structures are silently truncated (not errored).
- Circular type references stop expansion regardless of depth limit.

## Error Handling

- GraphQL validation errors → MCP error response with descriptive message.
- GraphQL execution errors → MCP error response with error details (no stack traces).
- Network/infrastructure errors → MCP error response with generic message (no internals leaked).
- Unhandled exceptions → caught, logged, generic MCP error returned.

## Auth Passthrough

When `Authorization.Mode = Passthrough`:
- HTTP `Authorization` header from the MCP request is forwarded to the GraphQL executor.
- No token validation is performed by graphql-mcp itself.
- The GraphQL server handles auth as it normally would.

When `Authorization.Mode = None`:
- No auth headers are forwarded.
- The GraphQL executor receives unauthenticated requests.
