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
  private final String transportMode;

  public McpController(
      GraphQLMCPServer server, ToolExecutor toolExecutor, List<ToolDescriptor> tools) {
    this(server, toolExecutor, tools, "streamable-http");
  }

  public McpController(
      GraphQLMCPServer server,
      ToolExecutor toolExecutor,
      List<ToolDescriptor> tools,
      String transportMode) {
    this.server = server;
    this.toolExecutor = toolExecutor;
    this.tools = tools;
    this.transportMode = transportMode == null ? "streamable-http" : transportMode;
  }

  @PostMapping(
      consumes = MediaType.APPLICATION_JSON_VALUE,
      produces = MediaType.APPLICATION_JSON_VALUE)
  public ResponseEntity<ObjectNode> handle(
      @RequestBody JsonNode body,
      @RequestHeader(value = "Mcp-Session-Id", required = false) String sessionId,
      HttpServletRequest request) {
    if (!"streamable-http".equalsIgnoreCase(transportMode)) {
      return ResponseEntity.status(HttpStatus.METHOD_NOT_ALLOWED).build();
    }

    ProtocolResponse protocolResponse = handleProtocol(body, sessionId, extractHeaders(request));
    ResponseEntity.BodyBuilder response = ResponseEntity.status(protocolResponse.status());
    if (protocolResponse.sessionId() != null) {
      response.header("Mcp-Session-Id", protocolResponse.sessionId());
    }
    return response.body(protocolResponse.body());
  }

  ProtocolResponse handleStdio(JsonNode body, String sessionId) {
    return handleProtocol(body, sessionId, Map.of());
  }

  private ProtocolResponse handleProtocol(
      JsonNode body, String sessionId, Map<String, String> headers) {
    if (!body.has("jsonrpc") || !body.has("method")) {
      return new ProtocolResponse(
          HttpStatus.BAD_REQUEST, jsonRpcError(body, -32600, "Invalid JSON-RPC request"), null);
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
        return new ProtocolResponse(
            HttpStatus.NOT_FOUND, jsonRpcError(body, -32000, "Unknown session"), null);
      }
      return new ProtocolResponse(
          HttpStatus.BAD_REQUEST,
          jsonRpcError(body, -32000, "Missing Mcp-Session-Id header"),
          null);
    }

    return switch (method) {
      case "tools/list" -> ok(handleToolsListResult(id));
      case "prompts/list" -> ok(buildPromptsListResult(id));
      case "prompts/get" -> handlePromptGetRpc(id, body.get("params"));
      case "resources/list" -> ok(buildResourcesListResult(id));
      case "resources/read" -> handleResourcesReadRpc(id, body.get("params"));
      case "catalog/list", "capabilities/catalog" -> ok(buildCatalogRpcResult(id));
      case "catalog/search", "capabilities/search" ->
          ok(buildCatalogSearchRpcResult(id, body.get("params")));
      case "tools/call" -> handleToolsCall(id, body.get("params"), headers);
      case "ping" -> ok(jsonRpcResult(id, MAPPER.createObjectNode()));
      default -> ok(jsonRpcError(body, -32601, "Method not found: " + method));
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
    if (!"streamable-http".equalsIgnoreCase(transportMode)) {
      return ResponseEntity.status(HttpStatus.METHOD_NOT_ALLOWED).build();
    }

    return ResponseEntity.ok(buildCatalogResult());
  }

  @GetMapping(
      path = "/.well-known/oauth-authorization-server",
      produces = MediaType.APPLICATION_JSON_VALUE)
  public ResponseEntity<ObjectNode> handleOAuthAuthorizationServerMetadata() {
    if (!"streamable-http".equalsIgnoreCase(transportMode)) {
      return ResponseEntity.status(HttpStatus.METHOD_NOT_ALLOWED).build();
    }

    ObjectNode metadata = buildOAuthAuthorizationServerMetadata();
    if (metadata == null) {
      return ResponseEntity.status(HttpStatus.NOT_FOUND).build();
    }

    return ResponseEntity.ok(metadata);
  }

  private ProtocolResponse handleInitialize(JsonNode id) {
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
    ObjectNode authorizationCap = MAPPER.createObjectNode();
    authorizationCap.put("mode", initResult.capabilities().authorization().mode());
    authorizationCap.set(
        "requiredScopes",
        MAPPER.valueToTree(initResult.capabilities().authorization().requiredScopes()));
    ObjectNode oauthCap = MAPPER.createObjectNode();
    oauthCap.put("metadata", initResult.capabilities().authorization().oauth2().metadata());
    if (initResult.capabilities().authorization().oauth2().metadata()) {
      oauthCap.put("resource", initResult.capabilities().authorization().oauth2().resource());
      oauthCap.put(
          "wellKnownPath", initResult.capabilities().authorization().oauth2().wellKnownPath());
    } else {
      oauthCap.putNull("resource");
      oauthCap.putNull("wellKnownPath");
    }
    authorizationCap.set("oauth2", oauthCap);
    capabilities.set("authorization", authorizationCap);
    result.set("capabilities", capabilities);

    ObjectNode serverInfo = MAPPER.createObjectNode();
    serverInfo.put("name", initResult.serverInfo().name());
    serverInfo.put("version", initResult.serverInfo().version());
    result.set("serverInfo", serverInfo);

    return new ProtocolResponse(HttpStatus.OK, jsonRpcResult(id, result), newSessionId);
  }

  private ObjectNode handleToolsListResult(JsonNode id) {
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
    return jsonRpcResult(id, result);
  }

  @SuppressWarnings("unchecked")
  private ProtocolResponse handleToolsCall(
      JsonNode id, JsonNode params, Map<String, String> requestHeaders) {
    if (params == null || !params.has("name")) {
      return ok(jsonRpcError(id, -32602, "Missing tool name"));
    }

    String toolName = params.get("name").asText();
    Map<String, Object> arguments = new HashMap<>();
    if (params.has("arguments")) {
      arguments = MAPPER.convertValue(params.get("arguments"), Map.class);
    }

    // Extract auth headers for passthrough
    Map<String, String> headers = new HashMap<>();
    String authHeader = requestHeaders.get("Authorization");
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

    return ok(jsonRpcResult(id, result));
  }

  private ProtocolResponse handlePromptGetRpc(JsonNode id, JsonNode params) {
    if (params == null || !params.hasNonNull("name")) {
      return ok(jsonRpcError(id, -32602, "Missing prompt name"));
    }

    String promptName = params.path("name").asText(null);
    if (promptName == null || promptName.isBlank()) {
      return ok(jsonRpcError(id, -32602, "Missing prompt name"));
    }

    JsonNode arguments = params.get("arguments");
    ObjectNode result;
    try {
      result = buildPromptGetResult(promptName, arguments);
    } catch (IllegalArgumentException ex) {
      return ok(jsonRpcError(id, -32602, ex.getMessage()));
    }

    if (result == null) {
      return ok(jsonRpcError(id, -32602, "Unknown prompt: " + promptName));
    }

    return ok(jsonRpcResult(id, result));
  }

  private ProtocolResponse handleResourcesReadRpc(JsonNode id, JsonNode params) {
    if (params == null || !params.hasNonNull("uri")) {
      return ok(jsonRpcError(id, -32602, "Missing resource uri"));
    }

    String uri = params.path("uri").asText(null);
    if (uri == null || uri.isBlank()) {
      return ok(jsonRpcError(id, -32602, "Missing resource uri"));
    }

    ObjectNode result = buildResourcesReadResult(uri);
    if (result == null) {
      return ok(jsonRpcError(id, -32602, "Unknown resource: " + uri));
    }

    return ok(jsonRpcResult(id, result));
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
    ObjectNode authorizationCap = MAPPER.createObjectNode();
    authorizationCap.put("mode", initResult.capabilities().authorization().mode());
    authorizationCap.set(
        "requiredScopes",
        MAPPER.valueToTree(initResult.capabilities().authorization().requiredScopes()));
    ObjectNode oauthCap = MAPPER.createObjectNode();
    oauthCap.put("metadata", initResult.capabilities().authorization().oauth2().metadata());
    if (initResult.capabilities().authorization().oauth2().metadata()) {
      oauthCap.put("resource", initResult.capabilities().authorization().oauth2().resource());
      oauthCap.put(
          "wellKnownPath", initResult.capabilities().authorization().oauth2().wellKnownPath());
    } else {
      oauthCap.putNull("resource");
      oauthCap.putNull("wellKnownPath");
    }
    authorizationCap.set("oauth2", oauthCap);
    capabilities.set("authorization", authorizationCap);
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

  private ObjectNode buildCatalogRpcResult(JsonNode id) {
    return jsonRpcResult(id, buildCatalogResult());
  }

  private ObjectNode buildResourcesListResult(JsonNode id) {
    return jsonRpcResult(id, buildResourcesListResult());
  }

  private ObjectNode buildPromptsListResult(JsonNode id) {
    return jsonRpcResult(id, buildPromptsListResult());
  }

  private ObjectNode buildCatalogSearchRpcResult(JsonNode id, JsonNode params) {
    return jsonRpcResult(id, buildCatalogSearchResult(parseSearchRequest(params)));
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
    prompts.add(
        promptSummary(
            "plan_task_workflow",
            "Plan Task Workflow",
            "Compose a discovery plan for a task using the reusable playbooks and catalog summaries.",
            argumentDefinition("task", "Plain-language task or goal to plan around.", true),
            argumentDefinition("domain", "Optional domain to focus the workflow on.", false)));
    prompts.add(
        promptSummary(
            "compare_tools_for_task",
            "Compare Tools For Task",
            "Compare the best candidate tools for a task before execution.",
            argumentDefinition(
                "task", "Plain-language task or goal to compare candidate tools for.", true),
            argumentDefinition("domain", "Optional domain to narrow the comparison.", false)));
    prompts.add(
        promptSummary(
            "prepare_tool_call",
            "Prepare Tool Call",
            "Review a tool summary and safe-call playbook before executing a specific tool.",
            argumentDefinition("tool", "Published MCP tool name to prepare for execution.", true),
            argumentDefinition("task", "Optional task context for the planned call.", false)));

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
      case "plan_task_workflow" -> buildPlanTaskWorkflowPrompt(arguments);
      case "compare_tools_for_task" -> buildCompareToolsPrompt(arguments);
      case "prepare_tool_call" -> buildPrepareToolCallPrompt(arguments);
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

  private ObjectNode buildPlanTaskWorkflowPrompt(JsonNode arguments) {
    String task = requiredPromptArgument(arguments, "task");
    String domain = optionalPromptArgument(arguments, "domain");
    String summaryResourceUri =
        domain == null || domain.isBlank()
            ? CATALOG_OVERVIEW_URI
            : CATALOG_DOMAIN_URI_PREFIX + domain;

    if (buildResourcesReadResult(summaryResourceUri) == null) {
      throw new IllegalArgumentException("Unknown resource: " + summaryResourceUri);
    }

    String instruction =
        domain == null || domain.isBlank()
            ? "A user wants to: "
                + task
                + "\n\nUse the embedded discovery playbook and catalog overview to propose the best step-by-step exploration workflow. Explain when to use resources/read, catalog/search, prompts/get, and tools/call, and identify what information the client should gather before execution."
            : "A user wants to: "
                + task
                + "\n\nThe likely domain is '"
                + domain
                + "'. Use the embedded domain investigation playbook and domain summary to propose the best step-by-step workflow. Explain when to read resources, search inside the domain, compare tools, and what arguments should be gathered before execution.";

    return promptResult(
        "Plan a reusable discovery workflow for a task using the advanced discovery packs.",
        instruction,
        domain == null || domain.isBlank()
            ? DISCOVERY_PACK_URI_PREFIX + "start-here"
            : DISCOVERY_PACK_URI_PREFIX + "investigate-domain",
        summaryResourceUri);
  }

  private ObjectNode buildCompareToolsPrompt(JsonNode arguments) {
    String task = requiredPromptArgument(arguments, "task");
    String domain = optionalPromptArgument(arguments, "domain");
    String summaryResourceUri =
        domain == null || domain.isBlank()
            ? CATALOG_OVERVIEW_URI
            : CATALOG_DOMAIN_URI_PREFIX + domain;

    if (buildResourcesReadResult(summaryResourceUri) == null) {
      throw new IllegalArgumentException("Unknown resource: " + summaryResourceUri);
    }

    String instruction =
        domain == null || domain.isBlank()
            ? "A user wants to: "
                + task
                + "\n\nUse the embedded discovery pack and catalog summary to compare the 2-3 best candidate tools. Explain the trade-offs between them, when catalog/search should be used first, and what arguments or filters are likely needed before execution."
            : "A user wants to: "
                + task
                + "\n\nThe likely domain is '"
                + domain
                + "'. Use the embedded discovery pack and domain summary to compare the strongest candidate tools in that domain. Explain the trade-offs, expected arguments, and the safest next step before a tool call.";

    return promptResult(
        "Compare likely candidate tools for a task before choosing one to execute.",
        instruction,
        domain == null || domain.isBlank()
            ? DISCOVERY_PACK_URI_PREFIX + "start-here"
            : DISCOVERY_PACK_URI_PREFIX + "investigate-domain",
        summaryResourceUri);
  }

  private ObjectNode buildPrepareToolCallPrompt(JsonNode arguments) {
    String toolName = requiredPromptArgument(arguments, "tool");
    String task = optionalPromptArgument(arguments, "task");
    String toolResourceUri = CATALOG_TOOL_URI_PREFIX + toolName;

    if (buildResourcesReadResult(toolResourceUri) == null) {
      throw new IllegalArgumentException("Unknown resource: " + toolResourceUri);
    }

    String instruction =
        task == null || task.isBlank()
            ? "Review the embedded safe-call playbook and tool summary for '"
                + toolName
                + "'. Identify the required arguments, any likely ambiguities, any follow-up discovery steps still needed, and a safe execution plan before calling the tool."
            : "A user wants to: "
                + task
                + "\n\nReview the embedded safe-call playbook and tool summary for '"
                + toolName
                + "'. Identify the required arguments, any likely ambiguities, whether additional discovery is still needed, and a safe execution plan before calling the tool.";

    return promptResult(
        "Prepare a safe execution plan for '" + toolName + "' using the advanced resource packs.",
        instruction,
        DISCOVERY_PACK_URI_PREFIX + "safe-tool-call",
        toolResourceUri);
  }

  private ObjectNode promptResult(String description, String instruction, String... resourceUris) {

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

    for (String resourceUri : resourceUris) {
      ObjectNode resourceResult = buildResourcesReadResult(resourceUri);
      if (resourceResult == null) {
        throw new IllegalArgumentException("Unknown resource: " + resourceUri);
      }

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
    }

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

    if (shouldPublishAuthorizationMetadata()) {
      ObjectNode authMetadata = MAPPER.createObjectNode();
      authMetadata.put("uri", AUTHORIZATION_METADATA_URI);
      authMetadata.put("name", "Authorization Metadata");
      authMetadata.put(
          "description", "OAuth metadata and required scopes for authenticated MCP clients.");
      authMetadata.put("mimeType", "application/json");
      resources.add(authMetadata);
    }

    for (ResourcePackDefinition pack : buildDiscoveryPacks()) {
      ObjectNode packResource = MAPPER.createObjectNode();
      packResource.put("uri", DISCOVERY_PACK_URI_PREFIX + pack.name());
      packResource.put("name", "Discovery Pack: " + pack.title());
      packResource.put("description", pack.description());
      packResource.put("mimeType", "application/json");
      resources.add(packResource);
    }

    for (String domainName : buildGroupedTools().keySet()) {
      ObjectNode domainResource = MAPPER.createObjectNode();
      domainResource.put("uri", CATALOG_DOMAIN_URI_PREFIX + domainName);
      domainResource.put("name", "Domain Summary: " + domainName);
      domainResource.put("description", "Discovery summary for the '" + domainName + "' domain.");
      domainResource.put("mimeType", "application/json");
      resources.add(domainResource);
    }

    tools.stream()
        .sorted(Comparator.comparing(ToolDescriptor::name))
        .forEach(
            tool -> {
              ObjectNode toolResource = MAPPER.createObjectNode();
              toolResource.put("uri", CATALOG_TOOL_URI_PREFIX + tool.name());
              toolResource.put("name", "Tool Summary: " + tool.name());
              toolResource.put(
                  "description", "Execution-oriented summary for the '" + tool.name() + "' tool.");
              toolResource.put("mimeType", "application/json");
              resources.add(toolResource);
            });

    result.set("resources", resources);
    return result;
  }

  private ObjectNode buildResourcesReadResult(String uri) {
    String text;
    if (CATALOG_OVERVIEW_URI.equalsIgnoreCase(uri)) {
      text = buildCatalogResult().toString();
    } else if (AUTHORIZATION_METADATA_URI.equalsIgnoreCase(uri)) {
      ObjectNode authMetadata = buildAuthorizationMetadataResource();
      if (authMetadata == null) {
        return null;
      }
      text = authMetadata.toString();
    } else if (uri.regionMatches(
        true, 0, CATALOG_DOMAIN_URI_PREFIX, 0, CATALOG_DOMAIN_URI_PREFIX.length())) {
      String domainName = uri.substring(CATALOG_DOMAIN_URI_PREFIX.length());
      ObjectNode domainSummary = buildDomainResource(domainName);
      if (domainSummary == null) {
        return null;
      }
      text = domainSummary.toString();
    } else if (uri.regionMatches(
        true, 0, CATALOG_TOOL_URI_PREFIX, 0, CATALOG_TOOL_URI_PREFIX.length())) {
      String toolName = uri.substring(CATALOG_TOOL_URI_PREFIX.length());
      ObjectNode toolSummary = buildToolResource(toolName);
      if (toolSummary == null) {
        return null;
      }
      text = toolSummary.toString();
    } else if (uri.regionMatches(
        true, 0, DISCOVERY_PACK_URI_PREFIX, 0, DISCOVERY_PACK_URI_PREFIX.length())) {
      String packName = uri.substring(DISCOVERY_PACK_URI_PREFIX.length());
      ObjectNode packSummary = buildDiscoveryPackResource(packName);
      if (packSummary == null) {
        return null;
      }
      text = packSummary.toString();
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

  private boolean shouldPublishAuthorizationMetadata() {
    GraphQLMCPServer.AuthorizationMetadata authorizationMetadata = server.authorizationMetadata();
    return authorizationMetadata.enabled();
  }

  private ObjectNode buildAuthorizationMetadataResource() {
    if (!shouldPublishAuthorizationMetadata()) {
      return null;
    }

    GraphQLMCPServer.AuthorizationMetadata authorizationMetadata = server.authorizationMetadata();
    GraphQLMCPServer.OAuthMetadata metadata = authorizationMetadata.oauthMetadata();

    ObjectNode resource = MAPPER.createObjectNode();
    resource.put("kind", "authorizationMetadata");
    resource.put("mode", authorizationMetadata.mode());
    resource.set("requiredScopes", MAPPER.valueToTree(authorizationMetadata.requiredScopes()));
    resource.put("resource", AUTHORIZATION_METADATA_URI);
    resource.put("wellKnownPath", AUTHORIZATION_WELL_KNOWN_PATH);

    ObjectNode oauth2 = MAPPER.createObjectNode();
    putIfNotBlank(oauth2, "issuer", metadata.issuer());
    putIfNotBlank(oauth2, "authorizationEndpoint", metadata.authorizationEndpoint());
    putIfNotBlank(oauth2, "tokenEndpoint", metadata.tokenEndpoint());
    putIfNotBlank(oauth2, "registrationEndpoint", metadata.registrationEndpoint());
    putIfNotBlank(oauth2, "jwksUri", metadata.jwksUri());
    putIfNotBlank(oauth2, "serviceDocumentation", metadata.serviceDocumentation());
    oauth2.set("scopesSupported", MAPPER.valueToTree(authorizationMetadata.requiredScopes()));
    oauth2.set("responseTypesSupported", MAPPER.valueToTree(metadata.responseTypesSupported()));
    oauth2.set("grantTypesSupported", MAPPER.valueToTree(metadata.grantTypesSupported()));
    oauth2.set(
        "tokenEndpointAuthMethodsSupported",
        MAPPER.valueToTree(metadata.tokenEndpointAuthMethodsSupported()));
    resource.set("oauth2", oauth2);
    return resource;
  }

  private ObjectNode buildOAuthAuthorizationServerMetadata() {
    if (!shouldPublishAuthorizationMetadata()) {
      return null;
    }

    GraphQLMCPServer.AuthorizationMetadata authorizationMetadata = server.authorizationMetadata();
    GraphQLMCPServer.OAuthMetadata metadata = authorizationMetadata.oauthMetadata();

    ObjectNode document = MAPPER.createObjectNode();
    putIfNotBlank(document, "issuer", metadata.issuer());
    putIfNotBlank(document, "authorization_endpoint", metadata.authorizationEndpoint());
    putIfNotBlank(document, "token_endpoint", metadata.tokenEndpoint());
    putIfNotBlank(document, "registration_endpoint", metadata.registrationEndpoint());
    putIfNotBlank(document, "jwks_uri", metadata.jwksUri());
    putIfNotBlank(document, "service_documentation", metadata.serviceDocumentation());
    document.set("scopes_supported", MAPPER.valueToTree(authorizationMetadata.requiredScopes()));
    document.set("response_types_supported", MAPPER.valueToTree(metadata.responseTypesSupported()));
    document.set("grant_types_supported", MAPPER.valueToTree(metadata.grantTypesSupported()));
    document.set(
        "token_endpoint_auth_methods_supported",
        MAPPER.valueToTree(metadata.tokenEndpointAuthMethodsSupported()));

    ObjectNode extension = MAPPER.createObjectNode();
    extension.put("mode", authorizationMetadata.mode());
    extension.set("required_scopes", MAPPER.valueToTree(authorizationMetadata.requiredScopes()));
    extension.put("resource_uri", AUTHORIZATION_METADATA_URI);
    extension.put("well_known_path", AUTHORIZATION_WELL_KNOWN_PATH);
    document.set("x_graphql_mcp", extension);
    return document;
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

  private ObjectNode buildToolResource(String toolName) {
    ToolDescriptor tool =
        tools.stream()
            .filter(entry -> entry.name().equalsIgnoreCase(toolName))
            .findFirst()
            .orElse(null);
    if (tool == null) {
      return null;
    }

    ObjectNode resource = MAPPER.createObjectNode();
    resource.put("kind", "toolSummary");
    resource.put("name", tool.name());
    resource.put("description", tool.description());
    resource.put("domain", tool.domainGroup());
    resource.put("category", tool.category());
    resource.put("operationType", tool.operationType().name().toLowerCase(Locale.ROOT));
    resource.put("fieldName", tool.graphQLFieldName());
    resource.set("tags", MAPPER.valueToTree(tool.tags()));
    if (tool.semanticHints() != null) {
      resource.set("semanticHints", MAPPER.valueToTree(tool.semanticHints()));
    }

    resource.set("argumentMapping", MAPPER.valueToTree(tool.argumentMapping()));
    resource.set("inputSchema", MAPPER.valueToTree(tool.inputSchema()));

    List<String> requiredArguments = getRequiredArguments(tool.inputSchema());
    List<String> optionalArguments =
        getPropertyNames(tool.inputSchema()).stream()
            .filter(name -> !requiredArguments.contains(name))
            .toList();
    resource.set("requiredArguments", MAPPER.valueToTree(requiredArguments));
    resource.set("optionalArguments", MAPPER.valueToTree(optionalArguments));

    return resource;
  }

  private ObjectNode buildDiscoveryPackResource(String packName) {
    return switch (packName.toLowerCase(Locale.ROOT)) {
      case "start-here" ->
          discoveryPack(
              "start-here",
              "Discovery Pack: Start Here",
              "A reusable exploration playbook for unfamiliar schemas or tasks.",
              List.of(
                  "You are new to the schema or domain.",
                  "You need to map a broad task to the right domain or tool."),
              List.of("explore_catalog", "plan_task_workflow", "choose_tool_for_task"),
              List.of(CATALOG_OVERVIEW_URI),
              List.of(
                  packStep(
                      1,
                      "initialize",
                      "initialize",
                      null,
                      "Start the MCP session and discover supported capabilities."),
                  packStep(
                      2,
                      "review_overview",
                      "resources/read",
                      CATALOG_OVERVIEW_URI,
                      "Scan grouped domains, categories, and discovery metadata."),
                  packStep(
                      3,
                      "inspect_groups",
                      "catalog/list",
                      null,
                      "Review grouped domains before narrowing further."),
                  packStep(
                      4,
                      "narrow_candidates",
                      "catalog/search",
                      null,
                      "Search by task keywords, domain, or tags when multiple tools look plausible."),
                  packStep(
                      5,
                      "choose_flow",
                      "prompts/get",
                      "choose_tool_for_task",
                      "Let the client explain the best next tool before executing anything.")),
              null);
      case "investigate-domain" ->
          discoveryPack(
              "investigate-domain",
              "Discovery Pack: Investigate Domain",
              "A reusable playbook for drilling into one domain and comparing tools.",
              List.of(
                  "You already know the likely domain.",
                  "You need to compare multiple tools inside one domain."),
              List.of("explore_domain", "compare_tools_for_task", "plan_task_workflow"),
              List.of("graphql-mcp://catalog/domain/<domain>"),
              List.of(
                  packStep(
                      1,
                      "read_domain_summary",
                      "resources/read",
                      "graphql-mcp://catalog/domain/<domain>",
                      "Review categories, tags, semantic hints, and available tools for the domain."),
                  packStep(
                      2,
                      "domain_search",
                      "catalog/search",
                      null,
                      "Search within the domain when the summary still contains multiple plausible tools."),
                  packStep(
                      3,
                      "compare_candidates",
                      "prompts/get",
                      "compare_tools_for_task",
                      "Ask the client to compare the best candidate tools and call out trade-offs."),
                  packStep(
                      4,
                      "inspect_tool_summary",
                      "resources/read",
                      "graphql-mcp://catalog/tool/<tool>",
                      "Read the tool summary once a likely candidate emerges.")),
              null);
      case "safe-tool-call" ->
          discoveryPack(
              "safe-tool-call",
              "Discovery Pack: Safe Tool Call",
              "A reusable execution checklist before calling an MCP tool.",
              List.of(
                  "You have selected a likely tool and need to confirm arguments.",
                  "You want to avoid premature or unsafe execution."),
              List.of("prepare_tool_call", "choose_tool_for_task"),
              List.of("graphql-mcp://catalog/tool/<tool>"),
              List.of(
                  packStep(
                      1,
                      "read_tool_summary",
                      "resources/read",
                      "graphql-mcp://catalog/tool/<tool>",
                      "Inspect required arguments, optional arguments, and semantic hints."),
                  packStep(
                      2,
                      "prepare_execution",
                      "prompts/get",
                      "prepare_tool_call",
                      "Have the client produce a safe execution plan before tools/call."),
                  packStep(
                      3,
                      "execute_tool",
                      "tools/call",
                      null,
                      "Call the tool once arguments and ambiguities are resolved.")),
              List.of(
                  "Confirm the tool is the right match for the task.",
                  "List required arguments and note any missing user input.",
                  "Review optional filters that could narrow the result safely.",
                  "Decide whether another catalog/search step is needed before execution."));
      default -> null;
    };
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

  private void putIfNotBlank(ObjectNode node, String property, String value) {
    if (value != null && !value.isBlank()) {
      node.put(property, value);
    }
  }

  private String optionalText(JsonNode node, String propertyName) {
    if (node == null || !node.has(propertyName) || node.path(propertyName).isNull()) {
      return null;
    }

    String value = node.path(propertyName).asText(null);
    return value == null || value.isBlank() ? null : value;
  }

  private Map<String, String> extractHeaders(HttpServletRequest request) {
    Map<String, String> headers = new HashMap<>();
    String authorization = request.getHeader("Authorization");
    if (authorization != null && !authorization.isBlank()) {
      headers.put("Authorization", authorization);
    }
    return headers;
  }

  private ProtocolResponse ok(ObjectNode body) {
    return new ProtocolResponse(HttpStatus.OK, body, null);
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

  private ObjectNode discoveryPack(
      String pack,
      String title,
      String description,
      List<String> whenToUse,
      List<String> recommendedPrompts,
      List<String> recommendedResources,
      List<ObjectNode> steps,
      List<String> checklist) {
    ObjectNode resource = MAPPER.createObjectNode();
    resource.put("kind", "resourcePack");
    resource.put("pack", pack);
    resource.put("title", title);
    resource.put("description", description);
    resource.set("whenToUse", MAPPER.valueToTree(whenToUse));
    resource.set("recommendedPrompts", MAPPER.valueToTree(recommendedPrompts));
    resource.set("recommendedResources", MAPPER.valueToTree(recommendedResources));
    resource.set("steps", MAPPER.valueToTree(steps));
    if (checklist != null) {
      resource.set("checklist", MAPPER.valueToTree(checklist));
    }
    return resource;
  }

  private ObjectNode packStep(
      int order, String action, String method, String target, String purpose) {
    ObjectNode step = MAPPER.createObjectNode();
    step.put("order", order);
    step.put("action", action);
    step.put("method", method);
    if (target != null) {
      if ("prompts/get".equals(method)) {
        step.put("prompt", target);
      } else {
        step.put("target", target);
      }
    }
    step.put("purpose", purpose);
    return step;
  }

  private Map<String, List<ToolDescriptor>> buildGroupedTools() {
    Map<String, List<ToolDescriptor>> groupedTools = new TreeMap<>();
    for (ToolDescriptor tool : tools) {
      groupedTools.computeIfAbsent(tool.domainGroup(), key -> new ArrayList<>()).add(tool);
    }
    return groupedTools;
  }

  private static final String CATALOG_OVERVIEW_URI = "graphql-mcp://catalog/overview";
  private static final String AUTHORIZATION_METADATA_URI = "graphql-mcp://auth/metadata";
  private static final String AUTHORIZATION_WELL_KNOWN_PATH =
      ".well-known/oauth-authorization-server";
  private static final String CATALOG_DOMAIN_URI_PREFIX = "graphql-mcp://catalog/domain/";
  private static final String CATALOG_TOOL_URI_PREFIX = "graphql-mcp://catalog/tool/";
  private static final String DISCOVERY_PACK_URI_PREFIX = "graphql-mcp://packs/discovery/";

  private record SearchMatch(ToolDescriptor tool, int score) {}

  record ProtocolResponse(HttpStatus status, ObjectNode body, String sessionId) {}

  private record ResourcePackDefinition(String name, String title, String description) {}

  private List<ResourcePackDefinition> buildDiscoveryPacks() {
    return List.of(
        new ResourcePackDefinition(
            "start-here",
            "Start Here",
            "Reusable exploration playbook for unfamiliar tasks or schemas."),
        new ResourcePackDefinition(
            "investigate-domain",
            "Investigate Domain",
            "Reusable playbook for drilling into one domain and comparing tools."),
        new ResourcePackDefinition(
            "safe-tool-call",
            "Safe Tool Call",
            "Reusable execution checklist before calling a tool."));
  }

  private List<String> getRequiredArguments(Map<String, Object> inputSchema) {
    Object required = inputSchema.get("required");
    if (!(required instanceof List<?> requiredList)) {
      return List.of();
    }

    List<String> values = new ArrayList<>();
    for (Object item : requiredList) {
      if (item instanceof String value && !value.isBlank()) {
        values.add(value);
      }
    }
    return List.copyOf(values);
  }

  private List<String> getPropertyNames(Map<String, Object> inputSchema) {
    Object properties = inputSchema.get("properties");
    if (!(properties instanceof Map<?, ?> propertyMap)) {
      return List.of();
    }

    List<String> values = new ArrayList<>();
    for (Object key : propertyMap.keySet()) {
      if (key instanceof String value && !value.isBlank()) {
        values.add(value);
      }
    }
    values.sort(String::compareTo);
    return List.copyOf(values);
  }

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
