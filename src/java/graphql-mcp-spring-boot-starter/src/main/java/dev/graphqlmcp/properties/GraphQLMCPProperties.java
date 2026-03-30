package dev.graphqlmcp.properties;

import java.util.ArrayList;
import java.util.List;
import org.springframework.boot.context.properties.ConfigurationProperties;

/** Configuration properties for graphql-mcp. Bound from application.yml under graphql.mcp. */
@ConfigurationProperties(prefix = "graphql.mcp")
public class GraphQLMCPProperties {

  private boolean enabled = true;
  private String toolPrefix;
  private String namingPolicy = "verb-noun";
  private boolean allowMutations = false;
  private List<String> excludedFields = new ArrayList<>();
  private boolean requireDescriptions = false;
  private int minDescriptionLength = 0;
  private int maxOutputDepth = 3;
  private int maxToolCount = 50;
  private int maxArgumentCount = 25;
  private int maxArgumentComplexity = 75;
  private List<String> includedDomains = new ArrayList<>();
  private List<String> excludedDomains = new ArrayList<>();
  private String transport = "streamable-http";
  private Authorization authorization = new Authorization();

  // --- Getters & Setters ---

  public boolean isEnabled() {
    return enabled;
  }

  public void setEnabled(boolean enabled) {
    this.enabled = enabled;
  }

  public String getToolPrefix() {
    return toolPrefix;
  }

  public void setToolPrefix(String toolPrefix) {
    this.toolPrefix = toolPrefix;
  }

  public String getNamingPolicy() {
    return namingPolicy;
  }

  public void setNamingPolicy(String namingPolicy) {
    this.namingPolicy = namingPolicy;
  }

  public boolean isAllowMutations() {
    return allowMutations;
  }

  public void setAllowMutations(boolean allowMutations) {
    this.allowMutations = allowMutations;
  }

  public List<String> getExcludedFields() {
    return excludedFields;
  }

  public void setExcludedFields(List<String> excludedFields) {
    this.excludedFields = excludedFields;
  }

  public boolean isRequireDescriptions() {
    return requireDescriptions;
  }

  public void setRequireDescriptions(boolean requireDescriptions) {
    this.requireDescriptions = requireDescriptions;
  }

  public int getMinDescriptionLength() {
    return minDescriptionLength;
  }

  public void setMinDescriptionLength(int minDescriptionLength) {
    this.minDescriptionLength = minDescriptionLength;
  }

  public int getMaxOutputDepth() {
    return maxOutputDepth;
  }

  public void setMaxOutputDepth(int maxOutputDepth) {
    this.maxOutputDepth = maxOutputDepth;
  }

  public int getMaxToolCount() {
    return maxToolCount;
  }

  public void setMaxToolCount(int maxToolCount) {
    this.maxToolCount = maxToolCount;
  }

  public int getMaxArgumentCount() {
    return maxArgumentCount;
  }

  public void setMaxArgumentCount(int maxArgumentCount) {
    this.maxArgumentCount = maxArgumentCount;
  }

  public int getMaxArgumentComplexity() {
    return maxArgumentComplexity;
  }

  public void setMaxArgumentComplexity(int maxArgumentComplexity) {
    this.maxArgumentComplexity = maxArgumentComplexity;
  }

  public List<String> getIncludedDomains() {
    return includedDomains;
  }

  public void setIncludedDomains(List<String> includedDomains) {
    this.includedDomains = includedDomains;
  }

  public List<String> getExcludedDomains() {
    return excludedDomains;
  }

  public void setExcludedDomains(List<String> excludedDomains) {
    this.excludedDomains = excludedDomains;
  }

  public String getTransport() {
    return transport;
  }

  public void setTransport(String transport) {
    this.transport = transport;
  }

  public Authorization getAuthorization() {
    return authorization;
  }

  public void setAuthorization(Authorization authorization) {
    this.authorization = authorization;
  }

  public static class Authorization {
    private String mode = "none";

    public String getMode() {
      return mode;
    }

    public void setMode(String mode) {
      this.mode = mode;
    }
  }
}
