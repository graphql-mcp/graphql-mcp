# Getting Started (Java, Netflix DGS)

## Status

The DGS adapter is stable-ready in the repository and built on top of the shared Spring GraphQL integration. Publish it with the same stable Java version when you cut the stable Java line.

## Minimal Setup

Add the dependency and replace the placeholder with the version you are publishing:

```xml
<dependency>
    <groupId>dev.graphql-mcp</groupId>
    <artifactId>graphql-mcp-dgs</artifactId>
    <version>REPLACE_WITH_LATEST_VERSION</version>
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
