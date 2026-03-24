package dev.graphqlmcp.execution;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import dev.graphqlmcp.publishing.ToolDescriptor;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/** Executes MCP tool calls by mapping them to GraphQL queries. */
public class ToolExecutor {

  private static final Logger log = LoggerFactory.getLogger(ToolExecutor.class);
  private static final ObjectMapper MAPPER = new ObjectMapper();

  private final GraphQLExecutor graphQLExecutor;
  private final Map<String, ToolDescriptor> toolRegistry;

  public ToolExecutor(GraphQLExecutor graphQLExecutor, List<ToolDescriptor> tools) {
    this.graphQLExecutor = graphQLExecutor;
    this.toolRegistry = new HashMap<>();
    for (ToolDescriptor tool : tools) {
      toolRegistry.put(tool.name(), tool);
    }
  }

  /** Executes a tool by name with the given arguments. */
  public ToolResult execute(
      String toolName, Map<String, Object> arguments, Map<String, String> headers) {
    ToolDescriptor tool = toolRegistry.get(toolName);
    if (tool == null) {
      return ToolResult.error("Tool '" + toolName + "' not found.");
    }

    log.debug("Executing tool '{}' with query: {}", toolName, tool.graphQLQuery());

    // Map arguments to GraphQL variables
    Map<String, Object> variables = new HashMap<>();
    if (arguments != null) {
      for (var entry : arguments.entrySet()) {
        String varName = tool.argumentMapping().getOrDefault(entry.getKey(), entry.getKey());
        variables.put(varName, entry.getValue());
      }
    }

    var result = graphQLExecutor.execute(tool.graphQLQuery(), variables, headers);

    if (!result.isSuccess()) {
      String errorMsg =
          result.errors().stream()
              .map(GraphQLExecutor.GraphQLExecutionError::message)
              .reduce((a, b) -> a + "; " + b)
              .orElse("Unknown error");
      return ToolResult.error(errorMsg);
    }

    try {
      String json = MAPPER.writeValueAsString(result.data());
      return ToolResult.success(json);
    } catch (JsonProcessingException e) {
      return ToolResult.error("Failed to serialize result: " + e.getMessage());
    }
  }

  public record ToolResult(boolean isSuccess, String content, String errorMessage) {
    public static ToolResult success(String content) {
      return new ToolResult(true, content, null);
    }

    public static ToolResult error(String message) {
      return new ToolResult(false, null, message);
    }
  }
}
