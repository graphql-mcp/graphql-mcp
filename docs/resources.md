# Resources

graphql-mcp now exposes MCP resources for discovery-oriented summaries, tool-specific execution context, and reusable playbooks.

## Supported Methods

- `resources/list`
- `resources/read`

These resources complement `catalog/list` and `catalog/search`:

- `catalog/list` is the grouped live discovery response
- `catalog/search` is the ranked candidate finder
- `resources/read` gives a stable summary document that UIs can cache or render directly

## Resource Types

### Catalog Overview

- URI: `graphql-mcp://catalog/overview`
- MIME type: `application/json`

This resource returns:

- server info
- discovery capabilities
- domain count
- tool count
- grouped domain summaries

### Domain Summary

- URI pattern: `graphql-mcp://catalog/domain/<domain>`
- MIME type: `application/json`

This resource returns:

- the domain name
- categories
- tags
- aggregated semantic hints
- tool count
- tool summaries for that domain

### Tool Summary

- URI pattern: `graphql-mcp://catalog/tool/<tool>`
- MIME type: `application/json`

This resource returns:

- the published tool name
- description, domain, category, and operation type
- semantic hints and tags
- required and optional arguments
- argument mapping
- input schema

### Discovery Packs

- URI pattern: `graphql-mcp://packs/discovery/<pack>`
- MIME type: `application/json`

These resources return reusable playbooks such as:

- `start-here`
- `investigate-domain`
- `safe-tool-call`

Each pack includes:

- when to use it
- recommended prompts
- recommended resources
- ordered workflow steps
- optional execution checklist items

## Example Requests

```json
{
  "jsonrpc": "2.0",
  "id": 11,
  "method": "resources/list"
}
```

```json
{
  "jsonrpc": "2.0",
  "id": 12,
  "method": "resources/read",
  "params": {
    "uri": "graphql-mcp://catalog/overview"
  }
}
```

See [Exploration Workflow](exploration.md) and
[examples/discovery-workflow](../examples/discovery-workflow) for runnable request files.
