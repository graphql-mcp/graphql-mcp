# Prompts

graphql-mcp now exposes MCP prompts that help clients turn discovery metadata into guided exploration workflows.

## Supported Methods

- `prompts/list`
- `prompts/get`

These prompts are designed to work with the existing discovery stack:

- `tools/list`
- `resources/list`
- `resources/read`
- `catalog/list`
- `catalog/search`

## Shipped Prompt Templates

### `explore_catalog`

Reviews the catalog overview resource and asks the client to summarize:

- available domains
- likely starting points
- next catalog or tool actions

### `explore_domain`

Takes a required `domain` argument and embeds the matching
`graphql-mcp://catalog/domain/<domain>` resource.

Use it when a client already knows the likely domain and wants to:

- inspect the relevant tools
- identify common tasks
- gather likely arguments before calling a tool

### `choose_tool_for_task`

Takes:

- required `task`
- optional `domain`

It embeds either the catalog overview or a domain summary and asks the client to recommend the best next tool, explain why it fits, and identify likely required arguments.

### `plan_task_workflow`

Takes:

- required `task`
- optional `domain`

It embeds:

- the reusable discovery playbook resource
- either the catalog overview or a domain summary

Use it when a client should explain the best step-by-step discovery loop before execution.

### `compare_tools_for_task`

Takes:

- required `task`
- optional `domain`

It embeds the domain-oriented playbook plus either the full catalog or one domain summary and asks the client to compare the strongest candidate tools, their trade-offs, and the data still needed before a call.

### `prepare_tool_call`

Takes:

- required `tool`
- optional `task`

It embeds:

- the safe-call playbook resource
- the tool summary resource for the selected tool

Use it when a client has mostly chosen a tool and needs to verify required arguments, ambiguities, and safe next steps.

## Example Requests

```json
{
  "jsonrpc": "2.0",
  "id": 21,
  "method": "prompts/list"
}
```

```json
{
  "jsonrpc": "2.0",
  "id": 22,
  "method": "prompts/get",
  "params": {
    "name": "explore_domain",
    "arguments": {
      "domain": "book"
    }
  }
}
```

See [Exploration Workflow](exploration.md) and
[examples/discovery-workflow](../examples/discovery-workflow) for runnable request files.
