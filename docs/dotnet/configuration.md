# Configuration (.NET)

## Full Options Reference

```csharp
builder.Services.AddHotChocolateMcp(options =>
{
    // --- Naming ---
    options.ToolPrefix = "myapi";                    // Prefix for all tool names
    options.NamingPolicy = ToolNamingPolicy.VerbNoun; // VerbNoun | Raw | PrefixedRaw

    // --- Safety ---
    options.AllowMutations = false;                  // Allow mutation tools (default: false)
    options.ExcludedFields.Add("password");           // Exclude specific fields
    options.ExcludedFields.Add("ssn");
    options.ExcludedTypes.Add("AuditLog");           // Exclude operations involving these types

    // --- Limits ---
    options.MaxOutputDepth = 3;                      // Selection set depth (default: 3)
    options.MaxToolCount = 50;                       // Max tools published (default: 50)
    options.MaxArgumentCount = 25;                   // Max allowed arguments per published tool
    options.MaxArgumentComplexity = 75;              // Weighted input-shape complexity gate
    options.IncludeDescriptions = true;              // Include GraphQL descriptions (default: true)
    options.RequireDescriptionsForPublishedTools = false; // Skip undocumented operations
    options.MinDescriptionLength = 0;                // Require descriptions to be at least this long when present
    options.IncludedDomains.Add("order");            // Only publish inferred domains you allow
    options.ExcludedDomains.Add("admin");            // Skip inferred domains you do not want exposed

    // --- Auth ---
    options.Authorization.Mode = McpAuthMode.None;   // None | Passthrough
    options.Authorization.RequiredScopes.Add("api"); // Required OAuth scopes (future)

    // --- Transport ---
    options.Transport = McpTransport.StreamableHttp; // StreamableHttp (only option in v0.1)
});
```

## Registration Methods

### Method 1: Service Collection (recommended)

```csharp
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>();

builder.Services.AddHotChocolateMcp(options => { ... });

var app = builder.Build();
app.UseGraphQLMcp();
```

### Method 2: Builder Chain

```csharp
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMCP(options => { ... });

var app = builder.Build();
app.UseGraphQLMcp();
```

### Method 3: Zero Config

```csharp
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>();

builder.Services.AddHotChocolateMcp();

var app = builder.Build();
app.UseGraphQLMcp();
```

## Endpoint Paths

```csharp
// Custom paths
app.UseGraphQLMcp(
    graphqlPath: "/api/graphql",
    mcpPath: "/api/mcp"
);
```

## Naming Policy Details

### VerbNoun (default)

```
Query field "users"      → "get_users"
Query field "orderById"  → "get_orderById"
Mutation "createUser"    → "createUser"
With prefix "v1"         → "v1_get_users"
```

### Raw

```
Query field "users"      → "users"
Mutation "createUser"    → "createUser"
With prefix "v1"         → "v1_users"
```

### PrefixedRaw

Same as Raw but prefix is always applied. Without a prefix, identical to Raw.

## Options Summary

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ToolPrefix` | `string?` | `null` | Tool name prefix |
| `NamingPolicy` | `ToolNamingPolicy` | `VerbNoun` | How tool names are derived |
| `AllowMutations` | `bool` | `false` | Expose mutation fields as tools |
| `ExcludedFields` | `HashSet<string>` | `[]` | Fields to exclude |
| `ExcludedTypes` | `HashSet<string>` | `[]` | Types to exclude |
| `MaxOutputDepth` | `int` | `3` | Max selection set depth |
| `MaxToolCount` | `int` | `50` | Max published tools |
| `MaxArgumentCount` | `int` | `25` | Max argument count allowed for a published tool |
| `MaxArgumentComplexity` | `int` | `75` | Max weighted input complexity allowed for a published tool |
| `IncludeDescriptions` | `bool` | `true` | Include GraphQL descriptions |
| `RequireDescriptionsForPublishedTools` | `bool` | `false` | Skip operations with missing descriptions |
| `MinDescriptionLength` | `int` | `0` | Skip operations with descriptions shorter than this threshold |
| `IncludedDomains` | `HashSet<string>` | `[]` | Only publish tools from these inferred domains when non-empty |
| `ExcludedDomains` | `HashSet<string>` | `[]` | Skip tools from these inferred domains |
| `Authorization.Mode` | `McpAuthMode` | `None` | Auth mode |
| `Transport` | `McpTransport` | `StreamableHttp` | Transport protocol |
