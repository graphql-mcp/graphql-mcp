# Exploration Workflow

This guide walks through the same MCP discovery loop against any sample app in this repo:

1. `initialize`
2. `tools/list`
3. `prompts/list`
4. `prompts/get`
5. `resources/list`
6. `resources/read`
7. `catalog/list`
8. `catalog/search`
9. `tools/call`

All three sample servers expose the same GraphQL operations:

- `hello`
- `books`
- `bookByTitle`

That keeps the discovery experience consistent across Hot Chocolate, graphql-dotnet, and Spring GraphQL.

## Start A Sample Server

Pick one sample and start it locally:

### Hot Chocolate

```bash
dotnet run --project examples/dotnet-hotchocolate-minimal
```

### graphql-dotnet

```bash
dotnet run --project examples/dotnet-graphqldotnet-minimal
```

### Spring GraphQL

```bash
mvn -B -ntp -f examples/java-spring-minimal/pom.xml spring-boot:run
```

If your server runs on a different port, update the base URL in
[graphql-mcp.http](../examples/discovery-workflow/graphql-mcp.http).

## Use The Shared Request Assets

The repo now includes a reusable request pack in
[examples/discovery-workflow](../examples/discovery-workflow):

- [graphql-mcp.http](../examples/discovery-workflow/graphql-mcp.http) for REST Client style workflows
- [initialize.json](../examples/discovery-workflow/initialize.json)
- [tools-list.json](../examples/discovery-workflow/tools-list.json)
- [prompts-list.json](../examples/discovery-workflow/prompts-list.json)
- [prompts-get-explore-catalog.json](../examples/discovery-workflow/prompts-get-explore-catalog.json)
- [prompts-get-explore-domain.json](../examples/discovery-workflow/prompts-get-explore-domain.json)
- [prompts-get-choose-tool.json](../examples/discovery-workflow/prompts-get-choose-tool.json)
- [prompts-get-plan-task-workflow.json](../examples/discovery-workflow/prompts-get-plan-task-workflow.json)
- [prompts-get-compare-tools.json](../examples/discovery-workflow/prompts-get-compare-tools.json)
- [prompts-get-prepare-tool-call.json](../examples/discovery-workflow/prompts-get-prepare-tool-call.json)
- [resources-list.json](../examples/discovery-workflow/resources-list.json)
- [resources-read-overview.json](../examples/discovery-workflow/resources-read-overview.json)
- [resources-read-book-domain.json](../examples/discovery-workflow/resources-read-book-domain.json)
- [resources-read-book-tool.json](../examples/discovery-workflow/resources-read-book-tool.json)
- [resources-read-start-pack.json](../examples/discovery-workflow/resources-read-start-pack.json)
- [resources-read-safe-call-pack.json](../examples/discovery-workflow/resources-read-safe-call-pack.json)
- [catalog-list.json](../examples/discovery-workflow/catalog-list.json)
- [catalog-search-book.json](../examples/discovery-workflow/catalog-search-book.json)
- [tools-call-hello.json](../examples/discovery-workflow/tools-call-hello.json)
- [tools-call-book-by-title.json](../examples/discovery-workflow/tools-call-book-by-title.json)

You can use those files with `curl`, Postman, VS Code REST Client, or any MCP proxy/debug tool.

## Recommended Exploration Loop

### 1. Initialize

Send [initialize.json](../examples/discovery-workflow/initialize.json) to `/mcp`.

Expected outcome:

- `Mcp-Session-Id` response header
- `capabilities.catalog.list = true`
- `capabilities.catalog.search = true`

### 2. List Tools

Send [tools-list.json](../examples/discovery-workflow/tools-list.json) with the session header.

Expected outcome:

- tool names such as `get_hello`, `get_books`, and `get_bookByTitle`
- per-tool `domain`, `category`, `tags`, and `semanticHints`

### 3. List Prompt Templates

Send [prompts-list.json](../examples/discovery-workflow/prompts-list.json).

Expected outcome:

- reusable workflow templates such as `explore_catalog`, `explore_domain`, and `choose_tool_for_task`
- advanced workflow templates such as `plan_task_workflow`, `compare_tools_for_task`, and `prepare_tool_call`
- structured prompt arguments instead of free-form client conventions

### 4. Fetch A Prompt

Send [prompts-get-explore-catalog.json](../examples/discovery-workflow/prompts-get-explore-catalog.json),
[prompts-get-explore-domain.json](../examples/discovery-workflow/prompts-get-explore-domain.json), or
[prompts-get-choose-tool.json](../examples/discovery-workflow/prompts-get-choose-tool.json).

Expected outcome:

- a reusable prompt message sequence
- embedded discovery resources inside the prompt payload
- a client-ready workflow for exploration or tool selection

### 5. List Discovery Resources

Send [resources-list.json](../examples/discovery-workflow/resources-list.json).

Expected outcome:

- a catalog overview resource
- one domain summary resource per discovered domain
- one tool summary resource per published tool
- reusable discovery pack resources
- stable `graphql-mcp://...` resource URIs that a client can read later

### 6. Read A Resource Summary

Send [resources-read-overview.json](../examples/discovery-workflow/resources-read-overview.json) or
[resources-read-book-domain.json](../examples/discovery-workflow/resources-read-book-domain.json).

Expected outcome:

- a cached-friendly JSON summary of the full catalog, a single domain, a single tool, or a reusable playbook
- grouped tool metadata without having to recompute a live search result

### 7. Inspect Catalog Groups

Send [catalog-list.json](../examples/discovery-workflow/catalog-list.json).

Expected outcome:

- grouped domains rather than a flat tool list
- a `book` domain with book-related tools
- aggregated `semanticHints` and tags for each domain

### 8. Search The Catalog

Send [catalog-search-book.json](../examples/discovery-workflow/catalog-search-book.json).

Expected outcome:

- ranked matches for book-oriented tools
- metadata that helps an exploration UI choose between list and lookup operations

### 9. Call A Simple Tool

Send [tools-call-hello.json](../examples/discovery-workflow/tools-call-hello.json).

Expected outcome:

- a successful text result containing `Hello, Claude!`

### 10. Call A Discovered Book Tool

Send [tools-call-book-by-title.json](../examples/discovery-workflow/tools-call-book-by-title.json).

Expected outcome:

- a result containing the matching sample book and nested author data

If you change naming policy or tool prefix, use `tools/list` or `catalog/search` first and update the tool name in the request body accordingly.

## Why This Workflow Matters

This loop shows the intended client experience for larger schemas:

- `tools/list` is the raw capability surface
- `prompts/get` provides reusable workflows on top of that surface
- `resources/read` gives stable summary and playbook documents
- `catalog/list` gives grouped summaries
- `catalog/search` narrows the candidate set
- `tools/call` executes the selected operation

## Related Docs

- [Discovery](discovery.md)
- [Prompts](prompts.md)
- [Resources](resources.md)
- [Transports](transports.md)
- [Getting Started (.NET)](dotnet/getting-started.md)
- [Getting Started (Java)](java/getting-started.md)
