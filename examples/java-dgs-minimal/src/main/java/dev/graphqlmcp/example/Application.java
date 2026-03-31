package dev.graphqlmcp.example;

import dev.graphqlmcp.dgs.annotation.EnableDgsMCP;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

@EnableDgsMCP
@SpringBootApplication
public class Application {
  public static void main(String[] args) {
    SpringApplication.run(Application.class, args);
  }
}
