# Contributing to graphql-mcp

Thank you for your interest in contributing! This guide will help you get started.

## Development Setup

### Prerequisites

**For .NET development:**
- .NET 10 SDK
- An IDE (VS Code, Visual Studio, Rider)

**For Java development:**
- JDK 21
- Maven 3.9+
- An IDE (IntelliJ, VS Code, Eclipse)

### Clone and Build

```bash
git clone https://github.com/graphql-mcp/graphql-mcp.git
cd graphql-mcp

# .NET
dotnet build src/dotnet/GraphQL.MCP.sln
dotnet test src/dotnet/GraphQL.MCP.sln

# Java
mvn -f src/java/pom.xml clean verify
```

## Branch Strategy

```
main          ← stable, protected, releases tagged here
develop       ← active development, PRs merged here first
feature/*     ← feature branches
fix/*         ← bugfix branches
```

1. Fork the repository
2. Create a branch from `develop`: `git checkout -b feature/my-feature develop`
3. Make your changes
4. Ensure tests pass
5. Submit a PR to `develop`

## Code Style

### .NET
- Follow standard C# conventions
- Use `dotnet format` before committing
- Use `sealed` for classes that won't be inherited
- Use primary constructors where appropriate
- XML doc comments on public APIs

### Java
- Follow standard Java conventions
- Use records for data classes
- JavaDoc on public APIs

## Testing

- Every new feature needs tests
- Every bug fix needs a regression test
- Unit tests in `*Tests` projects/modules
- Integration tests for end-to-end flows

### .NET
```bash
dotnet test src/dotnet/GraphQL.MCP.sln --verbosity normal
```

### Java
```bash
mvn -f src/java/pom.xml test
```

## Pull Request Process

1. Update CHANGELOG.md with your changes
2. Ensure CI passes (automatic on PR)
3. Request review from a maintainer
4. Squash merge to `develop`

## Reporting Issues

- Use the bug report template for bugs
- Use the feature request template for enhancements
- Include reproduction steps and environment details

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
