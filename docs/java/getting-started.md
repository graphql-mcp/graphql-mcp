# Getting Started (Java)

## Status

The Java and Spring GraphQL track is stable-ready in the repository and published on Maven Central. Use the latest published version until you cut the stable tag.

- core execution, publishing, Spring Boot auto-configuration, Streamable HTTP transport, and stdio transport are implemented
- example app and tests exist in the repo

## Prerequisites

- Java 21
- Spring Boot 4.x
- Spring for GraphQL

## Modules

The current Java distribution uses two modules:

- `graphql-mcp-spring-boot-starter`
- `graphql-mcp-web`

## Minimal Setup

```java
package dev.graphqlmcp.example;

import dev.graphqlmcp.annotation.EnableGraphQLMCP;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

@EnableGraphQLMCP
@SpringBootApplication
public class Application {
  public static void main(String[] args) {
    SpringApplication.run(Application.class, args);
  }
}
```

Add a Spring GraphQL controller:

```java
package dev.graphqlmcp.example;

import org.springframework.graphql.data.method.annotation.Argument;
import org.springframework.graphql.data.method.annotation.QueryMapping;
import org.springframework.stereotype.Controller;

@Controller
public class BookController {

  @QueryMapping
  public String hello(@Argument String name) {
    return "Hello, " + (name != null ? name : "World") + "!";
  }
}
```

## Dependency Setup

Add the following dependencies to your `pom.xml` and replace the placeholder with the latest published version:

```xml
<properties>
    <graphql-mcp.version>REPLACE_WITH_LATEST_VERSION</graphql-mcp.version>
</properties>

<dependency>
    <groupId>dev.graphql-mcp</groupId>
    <artifactId>graphql-mcp-spring-boot-starter</artifactId>
    <version>${graphql-mcp.version}</version>
</dependency>
<dependency>
    <groupId>dev.graphql-mcp</groupId>
    <artifactId>graphql-mcp-web</artifactId>
    <version>${graphql-mcp.version}</version>
</dependency>
```

If you are running the example locally from this repository, install the Java reactor first:

```bash
mvn -B -ntp -f src/java/pom.xml -DskipTests install
```

Then you can build or run the example:

```bash
mvn -B -ntp -f examples/java-spring-minimal/pom.xml spring-boot:run
```

## What Happens

1. Spring Boot auto-configures graphql-mcp when `graphql.mcp.enabled=true`
2. The schema is introspected into a canonical operation list
3. Policy and naming rules are applied
4. MCP tool descriptors are published
5. MCP is served either over `POST /mcp` or through stdio, depending on `graphql.mcp.transport`

## Test It

Start the app:

```bash
mvn -B -ntp -f examples/java-spring-minimal/pom.xml spring-boot:run
```

Initialize the MCP session:

```bash
curl -X POST http://localhost:8080/mcp \
  -H "Content-Type: application/json" \
  -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}"
```

Copy the `Mcp-Session-Id` response header, then use it for subsequent requests:

```bash
curl -X POST http://localhost:8080/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: <session-id>" \
  -d "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"
```

```bash
curl -X POST http://localhost:8080/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: <session-id>" \
  -d "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"get_hello\",\"arguments\":{\"name\":\"Claude\"}}}"
```

## Current Limitations

- semantic ranking is still intentionally lightweight compared with a full semantic indexing layer
- cross-framework docs still focus on the shared surface first and framework-specific deep dives second

## Next Steps

- [Configuration](configuration.md)
- [Exploration Workflow](../exploration.md)
- [Security](../security.md)
- [Mapping](../mapping.md)
- [Architecture](../architecture.md)
