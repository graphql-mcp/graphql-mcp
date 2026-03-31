# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- graphql-dotnet adapter - full `IGraphQLSchemaSource` and `IGraphQLExecutor` implementation for graphql-dotnet v8
- `AddGraphQLDotNetMcp()` extension method for one-line DI registration
- graphql-dotnet example app (`examples/dotnet-graphqldotnet-minimal`)
- 22 new tests for graphql-dotnet adapter (schema source + executor)
- Selection set field exclusion - `ExcludedFields` patterns now filter nested type fields, not just root operations
- `IsFieldExcluded()` method on `IMcpPolicy` interface
- `RequireDescriptionsForPublishedTools` / `graphql.mcp.require-descriptions` policy option
- `MaxArgumentCount` / `graphql.mcp.max-argument-count` policy option
- basic discovery metadata on published tools via category/tag annotations
- grouped discovery metadata via explicit tool domains and `catalog/list` capability catalogs
- semantic discovery hints on published tools via `semanticHints.intent` and `semanticHints.keywords`
- stronger discovery heuristics for domain inference, singularization, and structural suffix stripping
- searchable capability catalogs via `catalog/search` / `capabilities/search` with ranked matches and filters
- curated exploration walkthrough doc and reusable JSON/HTTP request assets for the sample apps
- lightweight MCP resources via `resources/list` and `resources/read` for catalog overview and domain summaries
- MCP prompts via `prompts/list` and `prompts/get` for exploration and tool-selection workflows
- advanced prompt/resource packs including workflow planning, candidate comparison, safe-call preparation, tool summaries, and reusable discovery playbooks
- advanced policy controls including domain curation, minimum description length, and weighted argument complexity limits
- reusable policy presets and profiles with built-in balanced, curated, strict, and exploratory baselines
- shared policy packs for commerce, content, and operations-oriented schemas
- stronger domain inference that falls back through return types, wrapper members, field names, and descriptions
- OAuth 2.1 metadata discovery across `initialize`, `resources/read graphql-mcp://auth/metadata`, and `/.well-known/oauth-authorization-server`
- stdio transport for both .NET and Java using line-delimited JSON-RPC over stdin/stdout
- Netflix DGS adapter package, enablement annotation, example app, and CI smoke build
- dedicated docs for policies, adapters, and roadmap
- dedicated stable-release guidance, version-neutral install snippets, and release criteria documentation
### Fixed
- ISchema DI resolution crash - Hot Chocolate adapter now uses `IRequestExecutorResolver` instead of `ISchema` (not registered in HC DI container)
- Integer argument deserialization - added `TryGetInt32` check before `TryGetInt64` in `ToolExecutor.DeserializeJsonElement`
- C# ternary numeric widening - added explicit `(object)` casts to prevent implicit int/long/double coercion in switch expression
- Java packaging - internal `graphql-mcp-tests` module is now skipped for Central publishing
- GitHub publish workflows now create prereleases only for prerelease tags and reserve normal releases for stable versions

## [java-v0.1.0-alpha.3] - 2026-03-26

### Added
- Java/Spring track - core execution, publishing, web transport, Spring Boot auto-configuration, and a minimal example
- Java CI pipeline with build, test, formatting checks, and coverage
- Java publish pipeline to Maven Central with GPG signing and GitHub Release creation
- Java Spring minimal example app (`examples/java-spring-minimal`)

## [0.1.0-alpha.1] - 2026-03-21

### Added
- .NET Abstractions package - core contracts and models
- .NET Core engine - schema canonicalization, policy evaluation, tool publishing, execution
- .NET AspNetCore package - Streamable HTTP transport
- .NET Hot Chocolate adapter - schema extraction and execution bridge
- .NET GraphQL.NET adapter - scaffolded
- Java core module - schema introspection and tool mapping (scaffolded)
- Java Spring Boot starter - auto-configuration and properties binding (scaffolded)
- OpenTelemetry instrumentation - activity spans for canonicalize, policy, publish, and execute phases; metrics for invocations, errors, duration, published tool count
- Policy engine - field/type exclusion with glob patterns, allowlist (IncludedFields), mutation blocking, naming policies (VerbNoun, Raw, PrefixedRaw), depth limiting, tool count limiting
- Streamable HTTP transport - MCP spec 2025-06-18 compliant with Mcp-Session-Id session management
- Tool publisher - JSON Schema generation with argument descriptions, default values, InputObject required field detection, union/interface inline fragments, circular type protection, depth-limited selection sets
- Auth passthrough - forwards Authorization header from MCP client to Hot Chocolate executor via global state
- Comprehensive test suite - 78 tests: unit, integration, pipeline, transport, observability
- CI/CD pipelines - .NET CI, Java CI, NuGet publish, Maven Central publish
- Documentation - architecture, mapping, security, transports, observability, getting started, configuration
- Example apps - minimal and secure Hot Chocolate demos
- Cross-ecosystem specs - tool mapping rules, safety model, transport behavior

### Fixed
- OTel metric deduplication - removed duplicate counter/histogram recording from transport layer (ToolExecutor is the single source of truth)
- PublishedToolCount double-counting - removed duplicate Add call from service registration
- Fragile PolicyEngine cast - added `Apply()` to `IMcpPolicy` interface, eliminated runtime cast
- Sync-over-async startup - replaced `GetAwaiter().GetResult()` with `IHostedService` for async tool initialization
- Auth passthrough in Hot Chocolate executor - headers from `GraphQLExecutionRequest` are now forwarded as global state
- Removed unused `ModelContextProtocol` NuGet dependency from AspNetCore package
