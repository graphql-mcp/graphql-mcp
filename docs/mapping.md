# Tool Mapping

## How GraphQL Operations Become MCP Tools

graphql-mcp discovers GraphQL operations through schema introspection and transforms them into MCP tool descriptors that AI clients can understand and invoke.

## What Gets Mapped

| GraphQL Concept | MCP Concept |
|----------------|-------------|
| Query root field | MCP Tool (read operation) |
| Mutation root field | MCP Tool (write operation, opt-in) |
| Field arguments | Tool input parameters (JSON Schema) |
| Return type | Auto-generated selection set |
| Field description | Tool description |

## What Does NOT Get Mapped

- Subscription fields (not supported in v0.1)
- Nested fields (only root-level fields become tools)
- Introspection fields (`__schema`, `__type`)
- Fields starting with `__`

## Naming

### VerbNoun Policy (default)

| GraphQL Field | Tool Name |
|--------------|-----------|
| `users` | `get_users` |
| `userById` | `get_userById` |
| `createUser` | `createUser` (mutations keep original name) |
| `deleteOrder` | `deleteOrder` |

With `ToolPrefix = "myapi"`:

| GraphQL Field | Tool Name |
|--------------|-----------|
| `users` | `myapi_get_users` |

### Raw Policy

Uses GraphQL field name as-is, with optional prefix.

## Argument Mapping

GraphQL types map to JSON Schema:

| GraphQL Type | JSON Schema |
|-------------|-------------|
| `String` | `{ "type": "string" }` |
| `Int` | `{ "type": "integer" }` |
| `Float` | `{ "type": "number" }` |
| `Boolean` | `{ "type": "boolean" }` |
| `ID` | `{ "type": "string" }` |
| `[String]` | `{ "type": "array", "items": { "type": "string" } }` |
| `String!` | Added to `required` array |
| `MyEnum` | `{ "type": "string", "enum": ["A", "B", "C"] }` |
| `MyInput` | `{ "type": "object", "properties": { ... } }` |

## Selection Set Generation

The tool publisher auto-generates a GraphQL selection set for return types:

```graphql
# MaxOutputDepth = 3 (default)
query McpOperation($id: ID!) {
  userById(id: $id) {       # depth 1
    id                       # scalar — always included
    name                     # scalar — always included
    email                    # scalar — always included
    department {             # depth 2
      id
      name
      company {              # depth 3
        id
        name
        # employees { ... }  ← depth 4, not included
      }
    }
  }
}
```

### Rules
1. Scalars and enums at any level within depth limit are always included
2. Objects are expanded recursively up to `MaxOutputDepth`
3. Lists are included (the list itself doesn't add depth)
4. Circular references stop expansion (e.g., `User.friends → [User]`)
5. Union/Interface types include `__typename` and inline fragments

## Example: Full Mapping

Given this schema:

```graphql
type Query {
  """Search for books by title or author"""
  searchBooks(query: String!, limit: Int = 10): [Book!]!
}

type Book {
  id: ID!
  title: String!
  author: Author!
}

type Author {
  name: String!
  email: String
}
```

graphql-mcp generates:

**Tool Name:** `get_searchBooks`

**Description:** `Search for books by title or author`

**Input Schema:**
```json
{
  "type": "object",
  "properties": {
    "query": { "type": "string" },
    "limit": { "type": "integer" }
  },
  "required": ["query"],
  "additionalProperties": false
}
```

**Generated GraphQL Query:**
```graphql
query McpOperation($query: String!, $limit: Int) {
  searchBooks(query: $query, limit: $limit) {
    id
    title
    author {
      name
      email
    }
  }
}
```
