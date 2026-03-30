# Discovery

graphql-mcp exposes layered discovery surfaces for MCP clients and exploration UIs:

- `tools/list` for the full published tool set
- `resources/list` and `resources/read` for stable summary, tool, and playbook documents
- `catalog/list` for grouped domain summaries
- `catalog/search` for ranked discovery matches with optional filters

For a runnable end-to-end sequence, see [Exploration Workflow](exploration.md) and the
[shared request assets](../examples/discovery-workflow).

For reusable workflow templates layered on top of these discovery surfaces, see [Prompts](prompts.md).
For the resource types behind those workflows, see [Resources](resources.md).

## Discovery Metadata

Each published tool can carry:

- `domain`
- `category`
- `tags`
- `semanticHints.intent`
- `semanticHints.keywords`

That metadata is generated from the canonical GraphQL operation, policy output, and naming pipeline. It is intended to help clients choose among many tools without traversing the entire schema.

## Search Request

`catalog/search` accepts additive filters through JSON-RPC params:

```json
{
  "query": "book",
  "domain": "book",
  "category": "Book",
  "operationType": "query",
  "tags": ["query"],
  "limit": 10
}
```

All fields are optional.

- `query` does token-based ranking across names, descriptions, tags, and semantic hints
- `domain`, `category`, and `operationType` narrow the candidate set
- `tags` requires matching tags
- `limit` caps the number of returned matches

## Example

```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "method": "catalog/search",
  "params": {
    "query": "order",
    "tags": ["query"],
    "limit": 5
  }
}
```

Typical result shape:

```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "result": {
    "query": "order",
    "filters": {
      "domain": null,
      "category": null,
      "operationType": null,
      "tags": ["query"]
    },
    "totalMatches": 1,
    "domainCount": 1,
    "matches": [
      {
        "name": "get_order",
        "domain": "order",
        "category": "Order",
        "operationType": "query",
        "fieldName": "order",
        "tags": ["order", "query"],
        "semanticHints": {
          "intent": "retrieve",
          "keywords": ["order", "query"]
        },
        "score": 55
      }
    ]
  }
}
```

## Current Limitations

- Search is lightweight ranking, not full semantic search
- Scores are heuristic and meant for ordering, not for authorization or policy decisions
- Search only covers published tools after policy filtering

## Current Exploration Assets

The repo now includes a reusable exploration pack for the sample servers:

- [docs/exploration.md](exploration.md)
- [examples/discovery-workflow/graphql-mcp.http](../examples/discovery-workflow/graphql-mcp.http)
- raw JSON-RPC request bodies under [examples/discovery-workflow](../examples/discovery-workflow)

## Next

The next discovery step is stronger semantic ranking and large-schema grouping:

- stronger grouping for large schemas
- richer semantic ranking beyond the current heuristic search
- deeper domain inference for broad or ambiguous schemas
