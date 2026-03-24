package dev.graphqlmcp.example;

import java.util.List;
import org.springframework.graphql.data.method.annotation.Argument;
import org.springframework.graphql.data.method.annotation.QueryMapping;
import org.springframework.stereotype.Controller;

@Controller
public class BookController {

  private static final List<Book> BOOKS =
      List.of(
          new Book("The GraphQL Guide", new Author("John Resig", "john@example.com")),
          new Book("Learning MCP", new Author("Jane Smith", "jane@example.com")));

  @QueryMapping
  public String hello(@Argument String name) {
    return "Hello, " + (name != null ? name : "World") + "!";
  }

  @QueryMapping
  public List<Book> books() {
    return BOOKS;
  }

  @QueryMapping
  public Book bookByTitle(@Argument String title) {
    return BOOKS.stream()
        .filter(b -> b.title().toLowerCase().contains(title.toLowerCase()))
        .findFirst()
        .orElse(null);
  }

  public record Book(String title, Author author) {}

  public record Author(String name, String email) {}
}
