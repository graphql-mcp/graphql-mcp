# Adapters

graphql-mcp keeps the core publishing pipeline framework-agnostic and pushes framework-specific concerns into adapters.

## Adapter Model

Each adapter is responsible for:

- extracting a GraphQL schema into the canonical model
- executing generated GraphQL operations
- integrating with framework DI and HTTP or stdio hosting

The shared core is responsible for:

- policy evaluation
- tool naming
- discovery metadata and domain grouping
- JSON Schema generation
- GraphQL query generation
- tool execution orchestration

## Current Adapters

### Hot Chocolate (.NET)

- package: `GraphQL.MCP.HotChocolate`
- role: stable-ready adapter and reference implementation

### graphql-dotnet (.NET)

- package: `GraphQL.MCP.GraphQLDotNet`
- role: stable-ready cross-framework .NET adapter

### Spring GraphQL (Java)

- packages:
  - `dev.graphql-mcp:graphql-mcp-spring-boot-starter`
  - `dev.graphql-mcp:graphql-mcp-web`
- role: stable-ready Java adapter

### Netflix DGS (Java)

- package: `dev.graphql-mcp:graphql-mcp-dgs`
- role: stable-ready DGS adapter layered on the shared Spring GraphQL integration

## Planned Adapters

- broader Java ecosystem expansion after the first stable Java cycle

## Non-Goals

graphql-mcp is not trying to replace first-party framework experiences where they already exist. The adapter strategy is about portability, curation, and consistent behavior across frameworks.
