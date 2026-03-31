package dev.graphqlmcp.web;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.*;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.boot.ApplicationArguments;
import org.springframework.boot.ApplicationRunner;

/** Runs graphql-mcp over stdio for local MCP clients. */
public class GraphQLMCPStdioServer implements ApplicationRunner {

  private static final Logger log = LoggerFactory.getLogger(GraphQLMCPStdioServer.class);
  private static final ObjectMapper MAPPER = new ObjectMapper();

  private final McpController controller;

  public GraphQLMCPStdioServer(McpController controller) {
    this.controller = controller;
  }

  @Override
  public void run(ApplicationArguments args) throws Exception {
    log.info("Starting graphql-mcp stdio transport loop");
    run(new InputStreamReader(System.in), new OutputStreamWriter(System.out));
  }

  public void run(Reader input, Writer output) throws IOException {
    BufferedReader reader =
        input instanceof BufferedReader buffered ? buffered : new BufferedReader(input);
    BufferedWriter writer =
        output instanceof BufferedWriter buffered ? buffered : new BufferedWriter(output);

    String sessionId = null;
    String line;
    while ((line = reader.readLine()) != null) {
      if (line.isBlank()) {
        continue;
      }

      JsonNode request = MAPPER.readTree(line);
      McpController.ProtocolResponse response = controller.handleStdio(request, sessionId);
      if (response.sessionId() != null && !response.sessionId().isBlank()) {
        sessionId = response.sessionId();
      }

      writer.write(response.body().toString());
      writer.newLine();
      writer.flush();
    }
  }
}
