package dev.graphqlmcp.web;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.StringReader;
import java.io.StringWriter;
import org.junit.jupiter.api.Test;

class GraphQLMCPStdioServerTest {

  private static final ObjectMapper MAPPER = new ObjectMapper();

  @Test
  void stdio_runner_processes_initialize_and_tools_list() throws Exception {
    McpController controller = McpControllerTestSupport.createController();
    GraphQLMCPStdioServer server = new GraphQLMCPStdioServer(controller);

    StringReader input =
        new StringReader(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}\n"
                + "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}\n");
    StringWriter output = new StringWriter();

    server.run(input, output);

    String[] lines = output.toString().trim().split("\\R");
    assertEquals(2, lines.length);

    JsonNode initialize = MAPPER.readTree(lines[0]);
    JsonNode toolsList = MAPPER.readTree(lines[1]);
    assertEquals("2025-06-18", initialize.path("result").path("protocolVersion").asText());
    assertEquals(2, toolsList.path("result").path("tools").size());
  }
}
