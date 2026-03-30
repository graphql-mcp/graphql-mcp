# Discovery Workflow Assets

These files provide a shared MCP exploration sequence for the sample apps in this repo.

- [graphql-mcp.http](graphql-mcp.http) contains a step-by-step request sequence
- the `*.json` files are raw JSON-RPC request bodies for `curl`, Postman, or custom scripts

The assets assume the default sample operations:

- `hello`
- `books`
- `bookByTitle`

They now cover:

- `tools/list`
- `prompts/list`
- `prompts/get`
- `resources/list`
- `resources/read`
- `catalog/list`
- `catalog/search`
- `tools/call`

The advanced prompt/resource pack assets include:

- workflow-planning prompts
- candidate-comparison prompts
- safe-call preparation prompts
- tool summary resources
- reusable discovery playbook resources

See [docs/exploration.md](../../docs/exploration.md) for the full walkthrough.
