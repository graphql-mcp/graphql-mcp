package dev.graphqlmcp.web;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import dev.graphqlmcp.execution.ToolExecutor;
import dev.graphqlmcp.publishing.ToolDescriptor;
import dev.graphqlmcp.server.GraphQLMCPServer;
import jakarta.servlet.http.HttpServletRequest;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

/** Streamable HTTP MCP endpoint. Handles JSON-RPC 2.0 over POST /mcp. */
@RestController
@RequestMapping("${graphql.mcp.endpoint:/mcp}")
public class McpController {

  private static final Logger log = LoggerFactory.getLogger(McpController.class);
  private static final ObjectMapper MAPPER = new ObjectMapper();

  private final GraphQLMCPServer server;
  private final ToolExecutor toolExecutor;
  private final List<ToolDescriptor> tools;
  private final ConcurrentHashMap<String, Boolean> sessions = new ConcurrentHashMap<>();

  public McpController(
      GraphQLMCPServer server, ToolExecutor toolExecutor, List<ToolDescriptor> tools) {
    this.server = server;
    this.toolExecutor = toolExecutor;
    this.tools = tools;
  }

  @PostMapping(
      consumes = MediaType.APPLICATION_JSON_VALUE,
      produces = MediaType.APPLICATION_JSON_VALUE)
  public ResponseEntity<ObjectNode> handle(
      @RequestBody JsonNode body,
      @RequestHeader(value = "Mcp-Session-Id", required = false) String sessionId,
      HttpServletRequest request) {

    if (!body.has("jsonrpc") || !body.has("method")) {
      return ResponseEntity.badRequest()
          .body(jsonRpcError(body, -32600, "Invalid JSON-RPC request"));
    }

    String method = body.get("method").asText();
    JsonNode id = body.get("id");

    // Methods that don't require a session
    if ("initialize".equals(method)) {
      return handleInitialize(id);
    }

    // All other methods require a valid session
    if (sessionId == null || !sessions.containsKey(sessionId)) {
      if (sessionId != null) {
        return ResponseEntity.status(HttpStatus.NOT_FOUND)
            .body(jsonRpcError(body, -32000, "Unknown session"));
      }
      return ResponseEntity.badRequest()
          .body(jsonRpcError(body, -32000, "Missing Mcp-Session-Id header"));
    }

    return switch (method) {
      case "tools/list" -> handleToolsList(id);
      case "catalog/list", "capabilities/catalog" -> handleCatalogRpc(id);
      case "tools/call" -> handleToolsCall(id, body.get("params"), request);
      case "ping" -> handlePing(id);
      default -> ResponseEntity.ok(jsonRpcError(body, -32601, "Method not found: " + method));
    };
  }

  @GetMapping
  public ResponseEntity<Void> handleGet() {
    return ResponseEntity.status(HttpStatus.METHOD_NOT_ALLOWED).build();
  }

  @DeleteMapping
  public ResponseEntity<Void> handleDelete(
      @RequestHeader(value = "Mcp-Session-Id", required = false) String sessionId) {
    if (sessionId != null) {
      sessions.remove(sessionId);
    }
    return ResponseEntity.ok().build();
  }

  @GetMapping(
      path = {"/catalog", "/capabilities"},
      produces = MediaType.APPLICATION_JSON_VALUE)
  public ResponseEntity<ObjectNode> handleCatalog() {
    return ResponseEntity.ok(buildCatalogResult());
  }

  private ResponseEntity<ObjectNode> handleInitialize(JsonNode id) {
    String newSessionId = UUID.randomUUID().toString().replace("-", "");
    sessions.put(newSessionId, true);

    GraphQLMCPServer.InitializeResult initResult = server.initialize();

    ObjectNode result = MAPPER.createObjectNode();
    result.put("protocolVersion", initResult.protocolVersion());

    ObjectNode capabilities = MAPPER.createObjectNode();
    ObjectNode toolsCap = MAPPER.createObjectNode();
    toolsCap.put("listChanged", initResult.capabilities().tools().listChanged());
    capabilities.set("tools", toolsCap);
    ObjectNode catalogCap = MAPPER.createObjectNode();
    catalogCap.put("list", initResult.capabilities().catalog().list());
    catalogCap.put("grouping", initResult.capabilities().catalog().grouping());
    capabilities.set("catalog", catalogCap);
    result.set("capabilities", capabilities);

    ObjectNode serverInfo = MAPPER.createObjectNode();
    serverInfo.put("name", initResult.serverInfo().name());
    serverInfo.put("version", initResult.serverInfo().version());
    result.set("serverInfo", serverInfo);

    return ResponseEntity.ok()
        .header("Mcp-Session-Id", newSessionId)
        .body(jsonRpcResult(id, result));
  }

  private ResponseEntity<ObjectNode> handleToolsList(JsonNode id) {
    ArrayNode toolsArray = MAPPER.createArrayNode();
    for (ToolDescriptor tool : tools) {
      ObjectNode toolNode = MAPPER.createObjectNode();
      toolNode.put("name", tool.name());
      toolNode.put("description", tool.description());
      ObjectNode annotations = MAPPER.createObjectNode();
      annotations.put("category", tool.category());
      annotations.set("tags", MAPPER.valueToTree(tool.tags()));
      annotations.put("domain", tool.domainGroup());
      if (tool.semanticHints() != null) {
        annotations.set("semanticHints", MAPPER.valueToTree(tool.semanticHints()));
      }
      toolNode.set("annotations", annotations);
      toolNode.set("inputSchema", MAPPER.valueToTree(tool.inputSchema()));
      toolsArray.add(toolNode);
    }

    ObjectNode result = MAPPER.createObjectNode();
    result.set("tools", toolsArray);
    return ResponseEntity.ok(jsonRpcResult(id, result));
  }

  @SuppressWarnings("unchecked")
  private ResponseEntity<ObjectNode> handleToolsCall(
      JsonNode id, JsonNode params, HttpServletRequest request) {
    if (params == null || !params.has("name")) {
      return ResponseEntity.ok(jsonRpcError(id, -32602, "Missing tool name"));
    }

    String toolName = params.get("name").asText();
    Map<String, Object> arguments = new HashMap<>();
    if (params.has("arguments")) {
      arguments = MAPPER.convertValue(params.get("arguments"), Map.class);
    }

    // Extract auth headers for passthrough
    Map<String, String> headers = new HashMap<>();
    String authHeader = request.getHeader("Authorization");
    if (authHeader != null) {
      headers.put("Authorization", authHeader);
    }

    ToolExecutor.ToolResult toolResult = toolExecutor.execute(toolName, arguments, headers);

    ObjectNode result = MAPPER.createObjectNode();
    ArrayNode content = MAPPER.createArrayNode();
    ObjectNode textContent = MAPPER.createObjectNode();
    textContent.put("type", "text");

    if (toolResult.isSuccess()) {
      textContent.put("text", toolResult.content());
    } else {
      textContent.put("text", toolResult.errorMessage());
      result.put("isError", true);
    }
    content.add(textContent);
    result.set("content", content);

    return ResponseEntity.ok(jsonRpcResult(id, result));
  }

  private ResponseEntity<ObjectNode> handlePing(JsonNode id) {
    return ResponseEntity.ok(jsonRpcResult(id, MAPPER.createObjectNode()));
  }

  private ResponseEntity<ObjectNode> handleCatalogRpc(JsonNode id) {
    return ResponseEntity.ok(jsonRpcResult(id, buildCatalogResult()));
  }

  private ObjectNode buildCatalogResult() {
    GraphQLMCPServer.InitializeResult initResult = server.initialize();

    ObjectNode result = MAPPER.createObjectNode();
    ObjectNode serverInfo = MAPPER.createObjectNode();
    serverInfo.put("name", initResult.serverInfo().name());
    serverInfo.put("version", initResult.serverInfo().version());
    result.set("serverInfo", serverInfo);

    ObjectNode capabilities = MAPPER.createObjectNode();
    ObjectNode toolsCap = MAPPER.createObjectNode();
    toolsCap.put("listChanged", initResult.capabilities().tools().listChanged());
    capabilities.set("tools", toolsCap);
    ObjectNode catalogCap = MAPPER.createObjectNode();
    catalogCap.put("list", initResult.capabilities().catalog().list());
    catalogCap.put("grouping", initResult.capabilities().catalog().grouping());
    capabilities.set("catalog", catalogCap);
    result.set("capabilities", capabilities);

    ArrayNode domains = MAPPER.createArrayNode();
    Map<String, List<ToolDescriptor>> groupedTools = new TreeMap<>();
    for (ToolDescriptor tool : tools) {
      groupedTools.computeIfAbsent(tool.domainGroup(), key -> new ArrayList<>()).add(tool);
    }

    for (Map.Entry<String, List<ToolDescriptor>> entry : groupedTools.entrySet()) {
      ObjectNode domainNode = MAPPER.createObjectNode();
      domainNode.put("domain", entry.getKey());
      domainNode.put("toolCount", entry.getValue().size());

      ArrayNode categories = MAPPER.createArrayNode();
      entry.getValue().stream()
          .map(ToolDescriptor::category)
          .filter(Objects::nonNull)
          .distinct()
          .sorted()
          .forEach(categories::add);
      domainNode.set("categories", categories);

      ArrayNode tags = MAPPER.createArrayNode();
      entry.getValue().stream()
          .flatMap(tool -> tool.tags().stream())
          .filter(Objects::nonNull)
          .distinct()
          .sorted()
          .forEach(tags::add);
      domainNode.set("tags", tags);

      ArrayNode toolNames = MAPPER.createArrayNode();
      entry.getValue().stream().map(ToolDescriptor::name).sorted().forEach(toolNames::add);
      domainNode.set("toolNames", toolNames);

      ArrayNode toolSummaries = MAPPER.createArrayNode();
      Set<String> domainIntents = new TreeSet<>();
      Set<String> domainKeywords = new TreeSet<>(String.CASE_INSENSITIVE_ORDER);
      entry.getValue().stream()
          .sorted(Comparator.comparing(ToolDescriptor::name))
          .forEach(
              tool -> {
                ObjectNode toolNode = MAPPER.createObjectNode();
                toolNode.put("name", tool.name());
                toolNode.put("description", tool.description());
                toolNode.put("category", tool.category());
                toolNode.put("operationType", tool.operationType().name().toLowerCase(Locale.ROOT));
                toolNode.put("fieldName", tool.graphQLFieldName());
                toolNode.set("tags", MAPPER.valueToTree(tool.tags()));
                if (tool.semanticHints() != null) {
                  toolNode.set("semanticHints", MAPPER.valueToTree(tool.semanticHints()));
                  if (tool.semanticHints().intent() != null
                      && !tool.semanticHints().intent().isBlank()) {
                    domainIntents.add(tool.semanticHints().intent());
                  }
                  for (String keyword : tool.semanticHints().keywords()) {
                    if (keyword != null && !keyword.isBlank()) {
                      domainKeywords.add(keyword);
                    }
                  }
                }
                toolSummaries.add(toolNode);
              });

      domainNode.set("tools", toolSummaries);
      if (!domainIntents.isEmpty() || !domainKeywords.isEmpty()) {
        ObjectNode semanticHints = MAPPER.createObjectNode();
        semanticHints.set("intents", MAPPER.valueToTree(domainIntents));
        semanticHints.set("keywords", MAPPER.valueToTree(domainKeywords));
        domainNode.set("semanticHints", semanticHints);
      }
      domains.add(domainNode);
    }

    result.put("domainCount", groupedTools.size());
    result.put("toolCount", tools.size());
    result.set("domains", domains);
    return result;
  }

  private ObjectNode jsonRpcResult(JsonNode id, ObjectNode result) {
    ObjectNode response = MAPPER.createObjectNode();
    response.put("jsonrpc", "2.0");
    if (id != null) response.set("id", id);
    response.set("result", result);
    return response;
  }

  private ObjectNode jsonRpcError(JsonNode body, int code, String message) {
    ObjectNode response = MAPPER.createObjectNode();
    response.put("jsonrpc", "2.0");
    if (body != null && body.has("id")) response.set("id", body.get("id"));
    ObjectNode error = MAPPER.createObjectNode();
    error.put("code", code);
    error.put("message", message);
    response.set("error", error);
    return response;
  }
}
