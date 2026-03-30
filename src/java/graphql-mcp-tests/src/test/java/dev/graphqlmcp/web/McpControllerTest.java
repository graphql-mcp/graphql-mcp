package dev.graphqlmcp.web;

import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.JsonNode;
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
    assertTrue(
        initializeResponse
            .getBody()
            .path("result")
            .path("capabilities")
            .path("catalog")
            .path("search")
            .asBoolean());
    assertTrue(
        initializeResponse
            .getBody()
            .path("result")
            .path("capabilities")
            .path("prompts")
            .path("listChanged")
            .asBoolean());
    assertTrue(
        initializeResponse
            .getBody()
            .path("result")
            .path("capabilities")
            .path("resources")
            .path("read")
            .asBoolean());

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
    assertEquals(2, listResponse.getBody().path("result").path("tools").size());
    var firstTool = listResponse.getBody().path("result").path("tools").get(0);
    assertEquals("api_get_hello", firstTool.path("name").asText());
    assertEquals("Query", firstTool.path("annotations").path("category").asText());
    assertEquals("hello", firstTool.path("annotations").path("domain").asText());
    assertEquals(
        "retrieve", firstTool.path("annotations").path("semanticHints").path("intent").asText());
    assertTrue(
        firstTool
            .path("annotations")
            .path("semanticHints")
            .path("keywords")
            .toString()
            .contains("hello"));
  }

  @Test
  void catalog_groups_tools_by_domain_without_session() {
    McpController controller = createController();

    var catalogResponse = controller.handleCatalog();

    assertEquals(HttpStatus.OK, catalogResponse.getStatusCode());
    assertEquals("graphql-mcp", catalogResponse.getBody().path("serverInfo").path("name").asText());
    assertTrue(
        catalogResponse.getBody().path("capabilities").path("catalog").path("list").asBoolean());
    assertEquals(2, catalogResponse.getBody().path("domains").size());

    var domains = catalogResponse.getBody().path("domains");
    var bookDomain = findDomain(domains, "book");
    assertEquals(1, bookDomain.path("toolCount").asInt());
    assertEquals("api_get_book", bookDomain.path("tools").get(0).path("name").asText());
    assertEquals("book", bookDomain.path("domain").asText());
    assertTrue(bookDomain.path("tools").get(0).path("tags").toString().contains("book"));
    assertTrue(bookDomain.path("semanticHints").path("intents").toString().contains("retrieve"));
    assertTrue(bookDomain.path("semanticHints").path("keywords").toString().contains("book"));
  }

  @Test
  void catalog_list_json_rpc_returns_grouped_tools() {
    McpController controller = createController();
    String sessionId =
        controller
            .handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest())
            .getHeaders()
            .getFirst("Mcp-Session-Id");

    var response =
        controller.handle(
            jsonRpcRequest("catalog/list", null), sessionId, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, response.getStatusCode());
    assertEquals(2, response.getBody().path("result").path("domainCount").asInt());
  }

  @Test
  void resources_list_json_rpc_returns_overview_and_domain_resources() {
    McpController controller = createController();
    String sessionId =
        controller
            .handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest())
            .getHeaders()
            .getFirst("Mcp-Session-Id");

    var response =
        controller.handle(
            jsonRpcRequest("resources/list", null), sessionId, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, response.getStatusCode());
    assertEquals(8, response.getBody().path("result").path("resources").size());
    assertEquals(
        "graphql-mcp://catalog/overview",
        response.getBody().path("result").path("resources").get(0).path("uri").asText());
    assertTrue(
        response
            .getBody()
            .path("result")
            .path("resources")
            .toString()
            .contains("graphql-mcp://packs/discovery/start-here"));
    assertTrue(
        response
            .getBody()
            .path("result")
            .path("resources")
            .toString()
            .contains("graphql-mcp://catalog/tool/api_get_book"));
  }

  @Test
  void prompts_list_json_rpc_returns_discovery_prompts() {
    McpController controller = createController();
    String sessionId =
        controller
            .handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest())
            .getHeaders()
            .getFirst("Mcp-Session-Id");

    var response =
        controller.handle(
            jsonRpcRequest("prompts/list", null), sessionId, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, response.getStatusCode());
    assertEquals(6, response.getBody().path("result").path("prompts").size());
    assertEquals(
        "explore_catalog",
        response.getBody().path("result").path("prompts").get(0).path("name").asText());
    assertTrue(
        response
            .getBody()
            .path("result")
            .path("prompts")
            .toString()
            .contains("plan_task_workflow"));
    assertTrue(
        response.getBody().path("result").path("prompts").toString().contains("prepare_tool_call"));
  }

  @Test
  void prompts_get_json_rpc_returns_embedded_resource_message() {
    McpController controller = createController();
    String sessionId =
        controller
            .handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest())
            .getHeaders()
            .getFirst("Mcp-Session-Id");

    ObjectNode params = MAPPER.createObjectNode();
    params.put("name", "explore_domain");
    ObjectNode arguments = MAPPER.createObjectNode();
    arguments.put("domain", "book");
    params.set("arguments", arguments);

    var response =
        controller.handle(
            jsonRpcRequest("prompts/get", params), sessionId, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, response.getStatusCode());
    assertTrue(response.getBody().path("result").path("description").asText().contains("book"));
    assertEquals(
        "resource",
        response
            .getBody()
            .path("result")
            .path("messages")
            .get(1)
            .path("content")
            .path("type")
            .asText());
    assertEquals(
        "graphql-mcp://catalog/domain/book",
        response
            .getBody()
            .path("result")
            .path("messages")
            .get(1)
            .path("content")
            .path("resource")
            .path("uri")
            .asText());
  }

  @Test
  void prompts_get_prepare_tool_call_returns_pack_and_tool_resources() {
    McpController controller = createController();
    String sessionId =
        controller
            .handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest())
            .getHeaders()
            .getFirst("Mcp-Session-Id");

    ObjectNode params = MAPPER.createObjectNode();
    params.put("name", "prepare_tool_call");
    ObjectNode arguments = MAPPER.createObjectNode();
    arguments.put("tool", "api_get_book");
    arguments.put("task", "fetch a book by title");
    params.set("arguments", arguments);

    var response =
        controller.handle(
            jsonRpcRequest("prompts/get", params), sessionId, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, response.getStatusCode());
    assertEquals(3, response.getBody().path("result").path("messages").size());
    assertEquals(
        "graphql-mcp://packs/discovery/safe-tool-call",
        response
            .getBody()
            .path("result")
            .path("messages")
            .get(1)
            .path("content")
            .path("resource")
            .path("uri")
            .asText());
    assertEquals(
        "graphql-mcp://catalog/tool/api_get_book",
        response
            .getBody()
            .path("result")
            .path("messages")
            .get(2)
            .path("content")
            .path("resource")
            .path("uri")
            .asText());
  }

  @Test
  void resources_read_json_rpc_returns_domain_summary() throws Exception {
    McpController controller = createController();
    String sessionId =
        controller
            .handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest())
            .getHeaders()
            .getFirst("Mcp-Session-Id");

    ObjectNode params = MAPPER.createObjectNode();
    params.put("uri", "graphql-mcp://catalog/domain/book");

    var response =
        controller.handle(
            jsonRpcRequest("resources/read", params), sessionId, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, response.getStatusCode());
    assertEquals(
        "graphql-mcp://catalog/domain/book",
        response.getBody().path("result").path("contents").get(0).path("uri").asText());

    JsonNode payload =
        MAPPER.readTree(
            response.getBody().path("result").path("contents").get(0).path("text").asText());
    assertEquals("domainSummary", payload.path("kind").asText());
    assertEquals("book", payload.path("domain").asText());
    assertEquals("api_get_book", payload.path("tools").get(0).path("name").asText());
  }

  @Test
  void resources_read_json_rpc_returns_tool_summary() throws Exception {
    McpController controller = createController();
    String sessionId =
        controller
            .handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest())
            .getHeaders()
            .getFirst("Mcp-Session-Id");

    ObjectNode params = MAPPER.createObjectNode();
    params.put("uri", "graphql-mcp://catalog/tool/api_get_book");

    var response =
        controller.handle(
            jsonRpcRequest("resources/read", params), sessionId, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, response.getStatusCode());

    JsonNode payload =
        MAPPER.readTree(
            response.getBody().path("result").path("contents").get(0).path("text").asText());
    assertEquals("toolSummary", payload.path("kind").asText());
    assertEquals("api_get_book", payload.path("name").asText());
    assertTrue(payload.path("argumentMapping").isObject());
  }

  @Test
  void resources_read_json_rpc_returns_discovery_pack() throws Exception {
    McpController controller = createController();
    String sessionId =
        controller
            .handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest())
            .getHeaders()
            .getFirst("Mcp-Session-Id");

    ObjectNode params = MAPPER.createObjectNode();
    params.put("uri", "graphql-mcp://packs/discovery/start-here");

    var response =
        controller.handle(
            jsonRpcRequest("resources/read", params), sessionId, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, response.getStatusCode());

    JsonNode payload =
        MAPPER.readTree(
            response.getBody().path("result").path("contents").get(0).path("text").asText());
    assertEquals("resourcePack", payload.path("kind").asText());
    assertEquals("start-here", payload.path("pack").asText());
    assertTrue(payload.path("recommendedPrompts").toString().contains("plan_task_workflow"));
  }

  @Test
  void catalog_search_json_rpc_returns_ranked_matches() {
    McpController controller = createController();
    String sessionId =
        controller
            .handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest())
            .getHeaders()
            .getFirst("Mcp-Session-Id");

    ObjectNode params = MAPPER.createObjectNode();
    params.put("query", "book");
    params.putArray("tags").add("query");
    params.put("limit", 1);

    var response =
        controller.handle(
            jsonRpcRequest("catalog/search", params), sessionId, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, response.getStatusCode());
    assertEquals(1, response.getBody().path("result").path("totalMatches").asInt());
    assertEquals(
        "api_get_book",
        response.getBody().path("result").path("matches").get(0).path("name").asText());
    assertTrue(response.getBody().path("result").path("matches").get(0).path("score").asInt() > 0);
  }

  @Test
  void capabilities_search_alias_returns_matches() {
    McpController controller = createController();
    String sessionId =
        controller
            .handle(jsonRpcRequest("initialize", null), null, new MockHttpServletRequest())
            .getHeaders()
            .getFirst("Mcp-Session-Id");

    ObjectNode params = MAPPER.createObjectNode();
    params.put("query", "hello");

    var response =
        controller.handle(
            jsonRpcRequest("capabilities/search", params), sessionId, new MockHttpServletRequest());

    assertEquals(HttpStatus.OK, response.getStatusCode());
    assertEquals(
        "api_get_hello",
        response.getBody().path("result").path("matches").get(0).path("name").asText());
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
    ToolDescriptor bookTool =
        new ToolDescriptor(
            "api_get_book",
            "Find a book",
            "Query",
            List.of("book", "query"),
            Map.of("type", "object"),
            "query($id: ID!) { book(id: $id) { title } }",
            "book",
            OperationType.QUERY,
            Map.of("id", "id"),
            "book",
            new ToolDescriptor.SemanticHints("retrieve", List.of("book", "query", "id")));
    ToolDescriptor tool =
        new ToolDescriptor(
            "api_get_hello",
            "Return a greeting",
            "Query",
            List.of("hello", "query"),
            Map.of("type", "object"),
            "query($name: String!) { hello(name: $name) }",
            "hello",
            OperationType.QUERY,
            Map.of("clientName", "name"),
            "hello",
            new ToolDescriptor.SemanticHints("retrieve", List.of("hello", "query", "name")));

    GraphQLExecutor executor = new GraphQLExecutor(TestSchemas.createExecutionSchema());
    ToolExecutor toolExecutor = new ToolExecutor(executor, List.of(tool, bookTool));
    GraphQLMCPServer server = new GraphQLMCPServer(List.of(tool, bookTool));
    return new McpController(server, toolExecutor, List.of(tool, bookTool));
  }

  private static ObjectNode findDomain(JsonNode domains, String name) {
    for (JsonNode domain : domains) {
      if (name.equals(domain.path("domain").asText())) {
        return (ObjectNode) domain;
      }
    }
    fail("Missing domain: " + name);
    return null;
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
