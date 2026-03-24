package dev.graphqlmcp.web;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import dev.graphqlmcp.TestSchemas;
import dev.graphqlmcp.execution.GraphQLExecutor;
import dev.graphqlmcp.execution.ToolExecutor;
import dev.graphqlmcp.introspection.GraphQLSchemaIntrospector.OperationType;
import dev.graphqlmcp.publishing.ToolDescriptor;
import dev.graphqlmcp.server.GraphQLMCPServer;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.http.HttpStatus;
import org.springframework.mock.web.MockHttpServletRequest;

class McpControllerTest {

  private static final ObjectMapper MAPPER = new ObjectMapper();

  @Test
  void initialize_creates_session_and_tools_list_requires_it() {
    McpController controller = createController();

    var initializeResponse =
        controller.handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, initializeResponse.getStatusCode());
    String sessionId = initializeResponse.getHeaders().getFirst("Mcp-Session-Id");
    assertNotNull(sessionId);
    assertFalse(sessionId.isBlank());

    var missingSessionResponse =
        controller.handle(jsonRpcRequest("tools/list", null), null, new MockHttpServletRequest());

    assertEquals(HttpStatus.BAD_REQUEST, missingSessionResponse.getStatusCode());
    assertEquals(-32000, missingSessionResponse.getBody().path("error").path("code").asInt());
    assertTrue(
        missingSessionResponse
            .getBody()
            .path("error")
            .path("message")
            .asText()
            .contains("Missing Mcp-Session-Id"));

    var listResponse =
        controller.handle(
            jsonRpcRequest("tools/list", null), sessionId, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, listResponse.getStatusCode());
    assertEquals(1, listResponse.getBody().path("result").path("tools").size());
    assertEquals(
        "api_get_hello",
        listResponse.getBody().path("result").path("tools").get(0).path("name").asText());
  }

  @Test
  void tools_call_executes_tool_and_forwards_auth_header() {
    McpController controller = createController();
    String sessionId =
        controller
            .handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest())
            .getHeaders()
            .getFirst("Mcp-Session-Id");

    ObjectNode arguments = MAPPER.createObjectNode();
    arguments.put("clientName", "Ada");
    ObjectNode params = MAPPER.createObjectNode();
    params.put("name", "api_get_hello");
    params.set("arguments", arguments);

    MockHttpServletRequest request = new MockHttpServletRequest();
    request.addHeader("Authorization", "Bearer 456");

    var callResponse = controller.handle(jsonRpcRequest("tools/call", params), sessionId, request);

    assertEquals(HttpStatus.OK, callResponse.getStatusCode());
    assertFalse(callResponse.getBody().path("result").path("isError").asBoolean());
    assertTrue(
        callResponse
            .getBody()
            .path("result")
            .path("content")
            .get(0)
            .path("text")
            .asText()
            .contains("Hello, Ada | auth=Bearer 456"));
  }

  private static McpController createController() {
    ToolDescriptor tool =
        new ToolDescriptor(
            "api_get_hello",
            "Return a greeting",
            Map.of("type", "object"),
            "query($name: String!) { hello(name: $name) }",
            "hello",
            OperationType.QUERY,
            Map.of("clientName", "name"));

    GraphQLExecutor executor = new GraphQLExecutor(TestSchemas.createExecutionSchema());
    ToolExecutor toolExecutor = new ToolExecutor(executor, List.of(tool));
    GraphQLMCPServer server = new GraphQLMCPServer(List.of(tool));
    return new McpController(server, toolExecutor, List.of(tool));
  }

  private static ObjectNode jsonRpcRequest(String method, ObjectNode params) {
    ObjectNode request = MAPPER.createObjectNode();
    request.put("jsonrpc", "2.0");
    request.put("id", 1);
    request.put("method", method);
    if (params != null) {
      request.set("params", params);
    }
    return request;
  }
}
