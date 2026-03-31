package dev.graphqlmcp.example;

import com.netflix.graphql.dgs.DgsComponent;
import com.netflix.graphql.dgs.DgsQuery;
import com.netflix.graphql.dgs.InputArgument;
import java.util.List;

@DgsComponent
public class ShowDataFetcher {

  @DgsQuery
  public String hello(@InputArgument String name) {
    return "Hello, " + (name != null ? name : "World") + "!";
  }

  @DgsQuery
  public Show showById(@InputArgument String id) {
    return new Show(id, "Stranger Things");
  }

  @DgsQuery
  public List<Show> shows() {
    return List.of(new Show("1", "Stranger Things"), new Show("2", "Wednesday"));
  }

  public record Show(String id, String title) {}
}
