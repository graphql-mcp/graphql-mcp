# Exploration Workflow

This guide walks through the same MCP discovery loop against any sample app in this repo:

1. `initialize`
2. `tools/list`
3. `catalog/list`
4. `catalog/search`
5. `tools/call`

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

### 3. Inspect Catalog Groups

Send [catalog-list.json](../examples/discovery-workflow/catalog-list.json).

Expected outcome:

- grouped domains rather than a flat tool list
- a `book` domain with book-related tools
- aggregated `semanticHints` and tags for each domain

### 4. Search The Catalog

Send [catalog-search-book.json](../examples/discovery-workflow/catalog-search-book.json).

Expected outcome:

- ranked matches for book-oriented tools
- metadata that helps an exploration UI choose between list and lookup operations

### 5. Call A Simple Tool

Send [tools-call-hello.json](../examples/discovery-workflow/tools-call-hello.json).

Expected outcome:

- a successful text result containing `Hello, Claude!`

### 6. Call A Discovered Book Tool

Send [tools-call-book-by-title.json](../examples/discovery-workflow/tools-call-book-by-title.json).

Expected outcome:

- a result containing the matching sample book and nested author data

If you change naming policy or tool prefix, use `tools/list` or `catalog/search` first and update the tool name in the request body accordingly.

## Why This Workflow Matters

This loop shows the intended client experience for larger schemas:

- `tools/list` is the raw capability surface
- `catalog/list` gives grouped summaries
- `catalog/search` narrows the candidate set
- `tools/call` executes the selected operation

That is the current discovery story before heavier MCP resources and prompts arrive later.

## Related Docs

- [Discovery](discovery.md)
- [Transports](transports.md)
- [Getting Started (.NET)](dotnet/getting-started.md)
- [Getting Started (Java)](java/getting-started.md)
