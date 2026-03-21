# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-03-21

### Added
- .NET Abstractions package — core contracts and models
- .NET Core engine — schema canonicalization, policy evaluation, tool publishing, execution
- .NET AspNetCore package — Streamable HTTP transport
- .NET Hot Chocolate adapter — schema extraction and execution bridge
- .NET GraphQL.NET adapter — scaffolded (coming in v0.2)
- Java core module — schema introspection and tool mapping (scaffolded)
- Java Spring Boot starter — auto-configuration and properties binding (scaffolded)
- OpenTelemetry instrumentation — activity spans for canonicalize, policy, publish, and execute phases; metrics for invocations, errors, duration, published tool count
- Policy engine — field/type exclusion with glob patterns, allowlist (IncludedFields), mutation blocking, naming policies (VerbNoun, Raw, PrefixedRaw), depth limiting, tool count limiting
- Streamable HTTP transport — MCP spec 2025-06-18 compliant with Mcp-Session-Id session management
- Tool publisher — JSON Schema generation with argument descriptions, default values, InputObject required field detection, union/interface inline fragments, circular type protection, depth-limited selection sets
- Auth passthrough — forwards Authorization header from MCP client to Hot Chocolate executor via global state
- Comprehensive test suite — 78 tests: unit, integration, pipeline, transport, observability
- CI/CD pipelines — .NET CI, Java CI, NuGet publish, Maven Central publish
- Documentation — architecture, mapping, security, transports, observability, getting started, configuration
- Example apps — minimal and secure Hot Chocolate demos
- Cross-ecosystem specs — tool mapping rules, safety model, transport behavior

### Fixed
- OTel metric deduplication — removed duplicate counter/histogram recording from transport layer (ToolExecutor is the single source of truth)
- PublishedToolCount double-counting — removed duplicate Add call from service registration
- Fragile PolicyEngine cast — added `Apply()` to `IMcpPolicy` interface, eliminated runtime cast
- Sync-over-async startup — replaced `GetAwaiter().GetResult()` with `IHostedService` for async tool initialization
- Auth passthrough in Hot Chocolate executor — headers from `GraphQLExecutionRequest` are now forwarded as global state
- Removed unused `ModelContextProtocol` NuGet dependency from AspNetCore package
