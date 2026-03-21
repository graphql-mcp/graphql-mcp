package dev.graphqlmcp.properties;

import org.springframework.boot.context.properties.ConfigurationProperties;

import java.util.ArrayList;
import java.util.List;

/**
 * Configuration properties for graphql-mcp.
 * Bound from application.yml under graphql.mcp.
 */
@ConfigurationProperties(prefix = "graphql.mcp")
public class GraphQLMCPProperties {

    private boolean enabled = true;
    private String toolPrefix;
    private String namingPolicy = "verb-noun";
    private boolean allowMutations = false;
    private List<String> excludedFields = new ArrayList<>();
    private int maxOutputDepth = 3;
    private int maxToolCount = 50;
    private String transport = "streamable-http";
    private Authorization authorization = new Authorization();

    // --- Getters & Setters ---

    public boolean isEnabled() { return enabled; }
    public void setEnabled(boolean enabled) { this.enabled = enabled; }

    public String getToolPrefix() { return toolPrefix; }
    public void setToolPrefix(String toolPrefix) { this.toolPrefix = toolPrefix; }

    public String getNamingPolicy() { return namingPolicy; }
    public void setNamingPolicy(String namingPolicy) { this.namingPolicy = namingPolicy; }

    public boolean isAllowMutations() { return allowMutations; }
    public void setAllowMutations(boolean allowMutations) { this.allowMutations = allowMutations; }

    public List<String> getExcludedFields() { return excludedFields; }
    public void setExcludedFields(List<String> excludedFields) { this.excludedFields = excludedFields; }

    public int getMaxOutputDepth() { return maxOutputDepth; }
    public void setMaxOutputDepth(int maxOutputDepth) { this.maxOutputDepth = maxOutputDepth; }

    public int getMaxToolCount() { return maxToolCount; }
    public void setMaxToolCount(int maxToolCount) { this.maxToolCount = maxToolCount; }

    public String getTransport() { return transport; }
    public void setTransport(String transport) { this.transport = transport; }

    public Authorization getAuthorization() { return authorization; }
    public void setAuthorization(Authorization authorization) { this.authorization = authorization; }

    public static class Authorization {
        private String mode = "none";

        public String getMode() { return mode; }
        public void setMode(String mode) { this.mode = mode; }
    }
}
