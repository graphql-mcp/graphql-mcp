# Getting Started (Java, Netflix DGS)

## Status

The DGS adapter is implemented in the repo and queued for the next Java alpha release. It is built on top of the shared Spring GraphQL integration.

## Minimal Setup

Add the dependency:

```xml
<dependency>
    <groupId>dev.graphql-mcp</groupId>
    <artifactId>graphql-mcp-dgs</artifactId>
    <version>0.1.0-SNAPSHOT</version>
</dependency>
```

Enable graphql-mcp on your DGS application:

```java
@EnableDgsMCP
@SpringBootApplication
public class Application {
  public static void main(String[] args) {
    SpringApplication.run(Application.class, args);
  }
}
```

Then add your normal DGS components and schema files. graphql-mcp reuses the GraphQL schema DGS builds and publishes the same MCP transport, discovery, resources, prompts, and policy surface as the Spring GraphQL adapter.

## Example

See [examples/java-dgs-minimal](../../examples/java-dgs-minimal) for a minimal DGS application that exposes `hello`, `showById`, and `shows` through MCP.

## Next Steps

- [Configuration](configuration.md)
- [Transports](../transports.md)
- [Exploration Workflow](../exploration.md)
