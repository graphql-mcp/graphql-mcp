# Security

## Design Philosophy

graphql-mcp is **safe by default**. The library ships with conservative defaults that prevent accidental exposure of dangerous operations or sensitive data. Every escalation requires explicit opt-in.

## Default Security Posture

| Setting | Default | Impact |
|---------|---------|--------|
| `AllowMutations` | `false` | Only read operations exposed |
| `MaxOutputDepth` | `3` | Prevents massive response payloads |
| `MaxToolCount` | `50` | Prevents tool overload in AI clients |
| `Authorization.Mode` | `None` | No auth forwarding |
| `ExcludedFields` | `[]` | Must be explicitly configured |
| `ExcludedTypes` | `[]` | Must be explicitly configured |

## Mutation Safety

Mutations are **OFF** by default. When an AI client connects, it can only see query operations.

To enable mutations:

```csharp
builder.Services.AddHotChocolateMcp(options =>
{
    options.AllowMutations = true;
});
```

When mutations are enabled:
- Tool descriptions are prefixed with `[MUTATION]` to signal write intent
- AI clients can use this signal to confirm with users before executing

## Field Exclusion

Exclude sensitive fields from tool generation:

```csharp
options.ExcludedFields.Add("password");
options.ExcludedFields.Add("ssn");
options.ExcludedFields.Add("internalNotes");
```

Exclude entire types (any operation involving these types is hidden):

```csharp
options.ExcludedTypes.Add("AuditLog");
options.ExcludedTypes.Add("AdminSettings");
```

Every exclusion is logged at `Information` level with the reason:

```
info: GraphQL.MCP.Core.Policy.PolicyEngine
      Excluding mutation 'deleteUser' — AllowMutations is false
info: GraphQL.MCP.Core.Policy.PolicyEngine
      Excluding field 'password' — listed in ExcludedFields
```

## Auth Passthrough

When `Authorization.Mode = Passthrough`, the `Authorization` header from MCP requests is forwarded to the GraphQL executor:

```csharp
options.Authorization.Mode = McpAuthMode.Passthrough;
```

This means:
- ✅ graphql-mcp does NOT validate tokens
- ✅ graphql-mcp does NOT store or log tokens
- ✅ The GraphQL server handles auth as it normally would
- ✅ Unauthenticated requests are forwarded as-is (the GraphQL server will reject them)

## Error Handling

graphql-mcp never leaks internal details through errors:

| Error Type | Returned to Client |
|-----------|-------------------|
| GraphQL validation error | Error message (no stack trace) |
| GraphQL execution error | Error messages from GraphQL response |
| Unhandled exception | Generic "internal error" message |
| Tool not found | "Tool '{name}' not found" |

Stack traces, internal class names, and file paths are never exposed.

## Best Practices

1. **Always configure `ExcludedFields`** for sensitive data fields
2. **Keep `AllowMutations = false`** unless you specifically need AI write access
3. **Use `McpAuthMode.Passthrough`** in production to ensure proper auth
4. **Set `MaxOutputDepth`** appropriate for your schema (3 is usually enough)
5. **Review published tools** using structured logs before deploying
6. **Monitor** tool invocations with OpenTelemetry
