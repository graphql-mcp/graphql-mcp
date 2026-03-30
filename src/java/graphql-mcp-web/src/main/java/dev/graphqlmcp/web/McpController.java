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
      case "prompts/list" -> handlePromptsListRpc(id);
      case "prompts/get" -> handlePromptGetRpc(id, body.get("params"));
      case "resources/list" -> handleResourcesListRpc(id);
      case "resources/read" -> handleResourcesReadRpc(id, body.get("params"));
      case "catalog/list", "capabilities/catalog" -> handleCatalogRpc(id);
      case "catalog/search", "capabilities/search" ->
          handleCatalogSearchRpc(id, body.get("params"));
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
    ObjectNode promptsCap = MAPPER.createObjectNode();
    promptsCap.put("listChanged", initResult.capabilities().prompts().listChanged());
    capabilities.set("prompts", promptsCap);
    ObjectNode resourcesCap = MAPPER.createObjectNode();
    resourcesCap.put("listChanged", initResult.capabilities().resources().listChanged());
    resourcesCap.put("read", initResult.capabilities().resources().read());
    capabilities.set("resources", resourcesCap);
    ObjectNode catalogCap = MAPPER.createObjectNode();
    catalogCap.put("list", initResult.capabilities().catalog().list());
    catalogCap.put("search", initResult.capabilities().catalog().search());
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

  private ResponseEntity<ObjectNode> handleResourcesListRpc(JsonNode id) {
    return ResponseEntity.ok(jsonRpcResult(id, buildResourcesListResult()));
  }

  private ResponseEntity<ObjectNode> handlePromptsListRpc(JsonNode id) {
    return ResponseEntity.ok(jsonRpcResult(id, buildPromptsListResult()));
  }

  private ResponseEntity<ObjectNode> handlePromptGetRpc(JsonNode id, JsonNode params) {
    if (params == null || !params.hasNonNull("name")) {
      return ResponseEntity.ok(jsonRpcError(id, -32602, "Missing prompt name"));
    }

    String promptName = params.path("name").asText(null);
    if (promptName == null || promptName.isBlank()) {
      return ResponseEntity.ok(jsonRpcError(id, -32602, "Missing prompt name"));
    }

    JsonNode arguments = params.get("arguments");
    ObjectNode result;
    try {
      result = buildPromptGetResult(promptName, arguments);
    } catch (IllegalArgumentException ex) {
      return ResponseEntity.ok(jsonRpcError(id, -32602, ex.getMessage()));
    }

    if (result == null) {
      return ResponseEntity.ok(jsonRpcError(id, -32602, "Unknown prompt: " + promptName));
    }

    return ResponseEntity.ok(jsonRpcResult(id, result));
  }

  private ResponseEntity<ObjectNode> handleResourcesReadRpc(JsonNode id, JsonNode params) {
    if (params == null || !params.hasNonNull("uri")) {
      return ResponseEntity.ok(jsonRpcError(id, -32602, "Missing resource uri"));
    }

    String uri = params.path("uri").asText(null);
    if (uri == null || uri.isBlank()) {
      return ResponseEntity.ok(jsonRpcError(id, -32602, "Missing resource uri"));
    }

    ObjectNode result = buildResourcesReadResult(uri);
    if (result == null) {
      return ResponseEntity.ok(jsonRpcError(id, -32602, "Unknown resource: " + uri));
    }

    return ResponseEntity.ok(jsonRpcResult(id, result));
  }

  private ResponseEntity<ObjectNode> handleCatalogSearchRpc(JsonNode id, JsonNode params) {
    return ResponseEntity.ok(
        jsonRpcResult(id, buildCatalogSearchResult(parseSearchRequest(params))));
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
    ObjectNode promptsCap = MAPPER.createObjectNode();
    promptsCap.put("listChanged", initResult.capabilities().prompts().listChanged());
    capabilities.set("prompts", promptsCap);
    ObjectNode resourcesCap = MAPPER.createObjectNode();
    resourcesCap.put("listChanged", initResult.capabilities().resources().listChanged());
    resourcesCap.put("read", initResult.capabilities().resources().read());
    capabilities.set("resources", resourcesCap);
    ObjectNode catalogCap = MAPPER.createObjectNode();
    catalogCap.put("list", initResult.capabilities().catalog().list());
    catalogCap.put("search", initResult.capabilities().catalog().search());
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

  private ObjectNode buildPromptsListResult() {
    ObjectNode result = MAPPER.createObjectNode();
    ArrayNode prompts = MAPPER.createArrayNode();

    prompts.add(
        promptSummary(
            "explore_catalog",
            "Explore Catalog",
            "Review the catalog overview resource and summarize the available domains, categories, and next discovery steps."));
    prompts.add(
        promptSummary(
            "explore_domain",
            "Explore Domain",
            "Review a specific domain summary resource and explain the most relevant tools for that domain.",
            argumentDefinition(
                "domain", "Domain name from catalog/list or resources/list.", true)));
    prompts.add(
        promptSummary(
            "choose_tool_for_task",
            "Choose Tool For Task",
            "Use the discovery metadata to recommend the best tool for a task and explain the required arguments.",
            argumentDefinition(
                "task", "Plain-language task or goal to match against the catalog.", true),
            argumentDefinition(
                "domain", "Optional domain to narrow the prompt to a known group.", false)));

    result.set("prompts", prompts);
    return result;
  }

  private ObjectNode buildPromptGetResult(String promptName, JsonNode arguments) {
    return switch (promptName) {
      case "explore_catalog" ->
          promptResult(
              "Explore the full catalog overview before choosing a tool.",
              "Review the embedded catalog overview. Summarize the available domains, highlight the most likely starting points, and suggest 2-3 next catalog or tool actions before executing anything.",
              CATALOG_OVERVIEW_URI);
      case "explore_domain" -> buildExploreDomainPrompt(arguments);
      case "choose_tool_for_task" -> buildChooseToolPrompt(arguments);
      default -> null;
    };
  }

  private ObjectNode buildExploreDomainPrompt(JsonNode arguments) {
    String domain = requiredPromptArgument(arguments, "domain");
    String resourceUri = CATALOG_DOMAIN_URI_PREFIX + domain;
    if (buildResourcesReadResult(resourceUri) == null) {
      throw new IllegalArgumentException("Unknown resource: " + resourceUri);
    }

    return promptResult(
        "Explore the '" + domain + "' domain and recommend the best next tool choices.",
        "Review the embedded domain summary for '"
            + domain
            + "'. Explain the domain's available tools, identify the strongest candidates for common tasks, and point out any arguments a client should gather before calling a tool.",
        resourceUri);
  }

  private ObjectNode buildChooseToolPrompt(JsonNode arguments) {
    String task = requiredPromptArgument(arguments, "task");
    String domain = optionalPromptArgument(arguments, "domain");
    String resourceUri =
        domain == null || domain.isBlank()
            ? CATALOG_OVERVIEW_URI
            : CATALOG_DOMAIN_URI_PREFIX + domain;

    if (buildResourcesReadResult(resourceUri) == null) {
      throw new IllegalArgumentException("Unknown resource: " + resourceUri);
    }

    String instruction =
        domain == null || domain.isBlank()
            ? "A user wants to: "
                + task
                + "\n\nReview the embedded catalog overview and recommend the best tool to call next. Explain why it fits, what arguments are likely required, and whether the client should narrow further with catalog/search before executing."
            : "A user wants to: "
                + task
                + "\n\nThe likely domain is '"
                + domain
                + "'. Review the embedded domain summary and recommend the best tool to call next. Explain why it fits, what arguments are likely required, and whether the client should still use catalog/search before executing.";

    return promptResult(
        "Recommend the most relevant tool for a task using the discovery summaries.",
        instruction,
        resourceUri);
  }

  private ObjectNode promptResult(String description, String instruction, String resourceUri) {
    ObjectNode resourceResult = buildResourcesReadResult(resourceUri);
    if (resourceResult == null) {
      return null;
    }

    ObjectNode result = MAPPER.createObjectNode();
    result.put("description", description);

    ArrayNode messages = MAPPER.createArrayNode();
    ObjectNode textMessage = MAPPER.createObjectNode();
    textMessage.put("role", "user");
    ObjectNode textContent = MAPPER.createObjectNode();
    textContent.put("type", "text");
    textContent.put("text", instruction);
    textMessage.set("content", textContent);
    messages.add(textMessage);

    JsonNode resourceContent = resourceResult.path("contents").get(0);
    ObjectNode resourceMessage = MAPPER.createObjectNode();
    resourceMessage.put("role", "user");
    ObjectNode resourceWrapper = MAPPER.createObjectNode();
    resourceWrapper.put("type", "resource");
    ObjectNode resourceNode = MAPPER.createObjectNode();
    resourceNode.put("uri", resourceContent.path("uri").asText());
    resourceNode.put("mimeType", resourceContent.path("mimeType").asText());
    resourceNode.put("text", resourceContent.path("text").asText());
    resourceWrapper.set("resource", resourceNode);
    resourceMessage.set("content", resourceWrapper);
    messages.add(resourceMessage);

    result.set("messages", messages);
    return result;
  }

  private ObjectNode buildResourcesListResult() {
    ObjectNode result = MAPPER.createObjectNode();
    ArrayNode resources = MAPPER.createArrayNode();

    ObjectNode overview = MAPPER.createObjectNode();
    overview.put("uri", CATALOG_OVERVIEW_URI);
    overview.put("name", "Catalog Overview");
    overview.put("description", "Grouped discovery summary for all published GraphQL MCP tools.");
    overview.put("mimeType", "application/json");
    resources.add(overview);

    for (String domainName : buildGroupedTools().keySet()) {
      ObjectNode domainResource = MAPPER.createObjectNode();
      domainResource.put("uri", CATALOG_DOMAIN_URI_PREFIX + domainName);
      domainResource.put("name", "Domain Summary: " + domainName);
      domainResource.put("description", "Discovery summary for the '" + domainName + "' domain.");
      domainResource.put("mimeType", "application/json");
      resources.add(domainResource);
    }

    result.set("resources", resources);
    return result;
  }

  private ObjectNode buildResourcesReadResult(String uri) {
    String text;
    if (CATALOG_OVERVIEW_URI.equalsIgnoreCase(uri)) {
      text = buildCatalogResult().toString();
    } else if (uri.regionMatches(
        true, 0, CATALOG_DOMAIN_URI_PREFIX, 0, CATALOG_DOMAIN_URI_PREFIX.length())) {
      String domainName = uri.substring(CATALOG_DOMAIN_URI_PREFIX.length());
      ObjectNode domainSummary = buildDomainResource(domainName);
      if (domainSummary == null) {
        return null;
      }
      text = domainSummary.toString();
    } else {
      return null;
    }

    ObjectNode result = MAPPER.createObjectNode();
    ArrayNode contents = MAPPER.createArrayNode();
    ObjectNode content = MAPPER.createObjectNode();
    content.put("uri", uri);
    content.put("mimeType", "application/json");
    content.put("text", text);
    contents.add(content);
    result.set("contents", contents);
    return result;
  }

  private ObjectNode buildDomainResource(String domainName) {
    List<ToolDescriptor> domainTools = buildGroupedTools().get(domainName);
    if (domainTools == null) {
      return null;
    }

    ObjectNode resource = MAPPER.createObjectNode();
    resource.put("kind", "domainSummary");
    resource.put("domain", domainName);
    resource.put("toolCount", domainTools.size());

    ArrayNode categories = MAPPER.createArrayNode();
    domainTools.stream()
        .map(ToolDescriptor::category)
        .filter(Objects::nonNull)
        .distinct()
        .sorted()
        .forEach(categories::add);
    resource.set("categories", categories);

    ArrayNode tags = MAPPER.createArrayNode();
    domainTools.stream()
        .flatMap(tool -> tool.tags().stream())
        .filter(Objects::nonNull)
        .distinct()
        .sorted()
        .forEach(tags::add);
    resource.set("tags", tags);

    TreeSet<String> intents = new TreeSet<>();
    TreeSet<String> keywords = new TreeSet<>(String.CASE_INSENSITIVE_ORDER);
    ArrayNode toolsNode = MAPPER.createArrayNode();
    domainTools.stream()
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
                  intents.add(tool.semanticHints().intent());
                }
                for (String keyword : tool.semanticHints().keywords()) {
                  if (keyword != null && !keyword.isBlank()) {
                    keywords.add(keyword);
                  }
                }
              }
              toolsNode.add(toolNode);
            });
    resource.set("tools", toolsNode);

    ObjectNode semanticHints = MAPPER.createObjectNode();
    semanticHints.set("intents", MAPPER.valueToTree(intents));
    semanticHints.set("keywords", MAPPER.valueToTree(keywords));
    resource.set("semanticHints", semanticHints);

    return resource;
  }

  private ObjectNode buildCatalogSearchResult(CatalogSearchRequest request) {
    List<SearchMatch> allMatches =
        tools.stream()
            .map(tool -> new SearchMatch(tool, scoreTool(tool, request)))
            .filter(match -> match.score() > 0)
            .sorted(
                Comparator.comparingInt(SearchMatch::score)
                    .reversed()
                    .thenComparing(match -> match.tool().name()))
            .toList();

    List<SearchMatch> matches = allMatches.stream().limit(request.limit()).toList();

    ObjectNode result = MAPPER.createObjectNode();
    if (request.query() != null) {
      result.put("query", request.query());
    } else {
      result.putNull("query");
    }

    ObjectNode filters = MAPPER.createObjectNode();
    if (request.domain() != null) {
      filters.put("domain", request.domain());
    } else {
      filters.putNull("domain");
    }
    if (request.category() != null) {
      filters.put("category", request.category());
    } else {
      filters.putNull("category");
    }
    if (request.operationType() != null) {
      filters.put("operationType", request.operationType());
    } else {
      filters.putNull("operationType");
    }
    filters.set("tags", MAPPER.valueToTree(request.tags()));
    result.set("filters", filters);

    ArrayNode matchesNode = MAPPER.createArrayNode();
    for (SearchMatch match : matches) {
      ToolDescriptor tool = match.tool();

      ObjectNode matchNode = MAPPER.createObjectNode();
      matchNode.put("name", tool.name());
      matchNode.put("description", tool.description());
      matchNode.put("domain", tool.domainGroup());
      matchNode.put("category", tool.category());
      matchNode.put("operationType", tool.operationType().name().toLowerCase(Locale.ROOT));
      matchNode.put("fieldName", tool.graphQLFieldName());
      matchNode.set("tags", MAPPER.valueToTree(tool.tags()));
      if (tool.semanticHints() != null) {
        matchNode.set("semanticHints", MAPPER.valueToTree(tool.semanticHints()));
      }
      matchNode.put("score", match.score());
      matchesNode.add(matchNode);
    }

    Map<String, List<ToolDescriptor>> groupedMatches = new TreeMap<>();
    for (SearchMatch match : allMatches) {
      ToolDescriptor tool = match.tool();
      groupedMatches.computeIfAbsent(tool.domainGroup(), ignored -> new ArrayList<>()).add(tool);
    }

    ArrayNode domains = MAPPER.createArrayNode();
    for (Map.Entry<String, List<ToolDescriptor>> entry : groupedMatches.entrySet()) {
      ObjectNode domainNode = MAPPER.createObjectNode();
      domainNode.put("domain", entry.getKey());
      domainNode.put("toolCount", entry.getValue().size());

      ArrayNode toolNames = MAPPER.createArrayNode();
      entry.getValue().stream().map(ToolDescriptor::name).sorted().forEach(toolNames::add);
      domainNode.set("toolNames", toolNames);

      ArrayNode tags = MAPPER.createArrayNode();
      entry.getValue().stream()
          .flatMap(tool -> tool.tags().stream())
          .filter(Objects::nonNull)
          .distinct()
          .sorted()
          .forEach(tags::add);
      domainNode.set("tags", tags);
      domains.add(domainNode);
    }

    result.put("totalMatches", allMatches.size());
    result.put("domainCount", groupedMatches.size());
    result.set("matches", matchesNode);
    result.set("domains", domains);
    return result;
  }

  private CatalogSearchRequest parseSearchRequest(JsonNode params) {
    if (params == null || !params.isObject()) {
      return new CatalogSearchRequest(null, null, null, null, List.of(), 20);
    }

    String query = optionalText(params, "query");
    String domain = optionalText(params, "domain");
    String category = optionalText(params, "category");
    String operationType = optionalText(params, "operationType");
    List<String> tags = optionalTextArray(params.get("tags"));
    int limit = params.has("limit") ? params.path("limit").asInt(20) : 20;
    if (limit <= 0) {
      limit = 20;
    }

    return new CatalogSearchRequest(
        query, domain, category, operationType, tags, Math.min(limit, 100));
  }

  private int scoreTool(ToolDescriptor tool, CatalogSearchRequest request) {
    if (!matchesFilter(tool.domainGroup(), request.domain())
        || !matchesFilter(tool.category(), request.category())
        || !matchesFilter(tool.operationType().name(), request.operationType())
        || !matchesTags(tool.tags(), request.tags())) {
      return 0;
    }

    List<String> tokens = tokenizeSearchText(request.query());
    if (tokens.isEmpty()) {
      return 1;
    }

    Set<String> exactValues = buildExactMatchSet(tool);
    List<String> searchableValues = buildSearchableValues(tool);
    int total = 0;
    for (String token : tokens) {
      int tokenScore = 0;
      if (exactValues.contains(token)) {
        tokenScore = 40;
      } else {
        for (String value : searchableValues) {
          if (value.contains(token)) {
            tokenScore = 15;
            break;
          }
        }
      }

      if (tokenScore == 0) {
        return 0;
      }

      total += tokenScore;
    }

    return total;
  }

  private Set<String> buildExactMatchSet(ToolDescriptor tool) {
    Set<String> values = new TreeSet<>(String.CASE_INSENSITIVE_ORDER);
    values.add(normalizeSearchValue(tool.name()));
    values.add(normalizeSearchValue(tool.graphQLFieldName()));
    values.add(normalizeSearchValue(tool.domainGroup()));
    values.add(normalizeSearchValue(tool.category()));
    values.add(normalizeSearchValue(tool.operationType().name()));

    for (String tag : tool.tags()) {
      values.add(normalizeSearchValue(tag));
    }

    if (tool.semanticHints() != null) {
      for (String keyword : tool.semanticHints().keywords()) {
        values.add(normalizeSearchValue(keyword));
      }
    }

    return values;
  }

  private List<String> buildSearchableValues(ToolDescriptor tool) {
    List<String> values = new ArrayList<>();
    values.add(tool.name().toLowerCase(Locale.ROOT));
    values.add(tool.graphQLFieldName().toLowerCase(Locale.ROOT));
    values.add(tool.description() == null ? "" : tool.description().toLowerCase(Locale.ROOT));
    values.add(tool.domainGroup().toLowerCase(Locale.ROOT));
    values.add(tool.category() == null ? "" : tool.category().toLowerCase(Locale.ROOT));
    values.add(String.join(" ", tool.tags()).toLowerCase(Locale.ROOT));
    if (tool.semanticHints() != null) {
      values.add(
          tool.semanticHints().intent() == null
              ? ""
              : tool.semanticHints().intent().toLowerCase(Locale.ROOT));
      values.add(String.join(" ", tool.semanticHints().keywords()).toLowerCase(Locale.ROOT));
    }
    return values;
  }

  private boolean matchesFilter(String actual, String expected) {
    return expected == null || expected.isBlank() || actual.equalsIgnoreCase(expected);
  }

  private boolean matchesTags(List<String> actualTags, List<String> requiredTags) {
    if (requiredTags.isEmpty()) {
      return true;
    }

    for (String required : requiredTags) {
      boolean found =
          actualTags.stream()
              .anyMatch(actual -> actual != null && actual.equalsIgnoreCase(required));
      if (!found) {
        return false;
      }
    }

    return true;
  }

  private List<String> tokenizeSearchText(String value) {
    if (value == null || value.isBlank()) {
      return List.of();
    }

    List<String> tokens = new ArrayList<>();
    StringBuilder current = new StringBuilder();
    for (int index = 0; index < value.length(); index++) {
      char c = value.charAt(index);
      if (Character.isLetterOrDigit(c)) {
        current.append(Character.toLowerCase(c));
      } else if (!current.isEmpty()) {
        tokens.add(current.toString());
        current.setLength(0);
      }
    }

    if (!current.isEmpty()) {
      tokens.add(current.toString());
    }

    return tokens;
  }

  private String normalizeSearchValue(String value) {
    if (value == null || value.isBlank()) {
      return "";
    }

    StringBuilder builder = new StringBuilder();
    for (int index = 0; index < value.length(); index++) {
      char c = value.charAt(index);
      if (Character.isLetterOrDigit(c)) {
        builder.append(Character.toLowerCase(c));
      }
    }
    return builder.toString();
  }

  private String optionalText(JsonNode node, String propertyName) {
    if (node == null || !node.has(propertyName) || node.path(propertyName).isNull()) {
      return null;
    }

    String value = node.path(propertyName).asText(null);
    return value == null || value.isBlank() ? null : value;
  }

  private List<String> optionalTextArray(JsonNode node) {
    if (node == null || node.isNull()) {
      return List.of();
    }

    if (node.isTextual()) {
      String value = node.asText();
      return value == null || value.isBlank() ? List.of() : List.of(value);
    }

    if (!node.isArray()) {
      return List.of();
    }

    List<String> values = new ArrayList<>();
    for (JsonNode item : node) {
      if (item.isTextual()) {
        String value = item.asText();
        if (value != null && !value.isBlank()) {
          values.add(value);
        }
      }
    }
    return List.copyOf(values);
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

  private record CatalogSearchRequest(
      String query,
      String domain,
      String category,
      String operationType,
      List<String> tags,
      int limit) {}

  private ObjectNode promptSummary(
      String name, String title, String description, ObjectNode... arguments) {
    ObjectNode prompt = MAPPER.createObjectNode();
    prompt.put("name", name);
    prompt.put("title", title);
    prompt.put("description", description);
    ArrayNode argumentArray = MAPPER.createArrayNode();
    for (ObjectNode argument : arguments) {
      argumentArray.add(argument);
    }
    prompt.set("arguments", argumentArray);
    return prompt;
  }

  private ObjectNode argumentDefinition(String name, String description, boolean required) {
    ObjectNode argument = MAPPER.createObjectNode();
    argument.put("name", name);
    argument.put("description", description);
    argument.put("required", required);
    return argument;
  }

  private Map<String, List<ToolDescriptor>> buildGroupedTools() {
    Map<String, List<ToolDescriptor>> groupedTools = new TreeMap<>();
    for (ToolDescriptor tool : tools) {
      groupedTools.computeIfAbsent(tool.domainGroup(), key -> new ArrayList<>()).add(tool);
    }
    return groupedTools;
  }

  private static final String CATALOG_OVERVIEW_URI = "graphql-mcp://catalog/overview";
  private static final String CATALOG_DOMAIN_URI_PREFIX = "graphql-mcp://catalog/domain/";

  private record SearchMatch(ToolDescriptor tool, int score) {}

  private String requiredPromptArgument(JsonNode arguments, String name) {
    String value = optionalPromptArgument(arguments, name);
    if (value == null || value.isBlank()) {
      throw new IllegalArgumentException("Missing required prompt argument: " + name);
    }
    return value;
  }

  private String optionalPromptArgument(JsonNode arguments, String name) {
    if (arguments == null || !arguments.hasNonNull(name)) {
      return null;
    }

    String value = arguments.path(name).asText(null);
    return value == null || value.isBlank() ? null : value;
  }
}
