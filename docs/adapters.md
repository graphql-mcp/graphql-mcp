# Adapters

graphql-mcp keeps the core publishing pipeline framework-agnostic and pushes framework-specific concerns into adapters.

## Adapter Model

Each adapter is responsible for:

- extracting a GraphQL schema into the canonical model
- executing generated GraphQL operations
- integrating with framework DI and HTTP hosting

The shared core is responsible for:

- policy evaluation
- tool naming
- JSON Schema generation
- GraphQL query generation
- tool execution orchestration

## Current Adapters

### Hot Chocolate (.NET)

- package: `GraphQL.MCP.HotChocolate`
- role: supported adapter and reference implementation

### graphql-dotnet (.NET)

- package: `GraphQL.MCP.GraphQLDotNet`
- role: primary cross-framework .NET adapter

### Spring GraphQL (Java)

- packages:
  - `dev.graphql-mcp:graphql-mcp-spring-boot-starter`
  - `dev.graphql-mcp:graphql-mcp-web`
- role: alpha-preview Java adapter

## Planned Adapters

- Netflix DGS
- broader Java ecosystem expansion after Spring hardening

## Non-Goals

graphql-mcp is not trying to replace first-party framework experiences where they already exist. The adapter strategy is about portability, curation, and consistent behavior across frameworks.
