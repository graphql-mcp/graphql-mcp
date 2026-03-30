# Discovery

graphql-mcp exposes three lightweight discovery surfaces for MCP clients and exploration UIs:

- `tools/list` for the full published tool set
- `catalog/list` for grouped domain summaries
- `catalog/search` for ranked discovery matches with optional filters

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

## Next

The next discovery step is richer exploration support:

- curated exploration examples
- stronger grouping for large schemas
- discovery-oriented resources and prompts
