package dev.graphqlmcp.server;

import dev.graphqlmcp.publishing.ToolDescriptor;
import java.util.List;

/**
 * Core MCP server that manages tool registration and protocol responses. Framework adapters
 * (Spring, etc.) delegate to this class.
 */
public class GraphQLMCPServer {

  private final List<ToolDescriptor> tools;
  private final AuthorizationMetadata authorizationMetadata;

  public GraphQLMCPServer(List<ToolDescriptor> tools) {
    this(tools, AuthorizationMetadata.none());
  }

  public GraphQLMCPServer(List<ToolDescriptor> tools, AuthorizationMetadata authorizationMetadata) {
    this.tools = List.copyOf(tools);
    this.authorizationMetadata = authorizationMetadata;
  }

  /** Returns the MCP initialize response capabilities. */
  public InitializeResult initialize() {
    return new InitializeResult(
        "2025-06-18",
        new Capabilities(
            new ToolCapabilities(true),
            new PromptCapabilities(true),
            new ResourceCapabilities(true, true),
            new CatalogCapabilities(true, true, "domain"),
            new AuthorizationCapabilities(
                authorizationMetadata.mode(),
                authorizationMetadata.requiredScopes(),
                new OAuth2Capabilities(
                    authorizationMetadata.enabled(),
                    AuthorizationMetadata.RESOURCE_URI,
                    AuthorizationMetadata.WELL_KNOWN_PATH))),
        new ServerInfo("graphql-mcp", "0.1.0"));
  }

  /** Returns all registered tools. */
  public List<ToolDescriptor> listTools() {
    return tools;
  }

  /** Checks if a tool exists. */
  public boolean hasTool(String name) {
    return tools.stream().anyMatch(t -> t.name().equals(name));
  }

  /** Returns the configured auth metadata for MCP discovery surfaces. */
  public AuthorizationMetadata authorizationMetadata() {
    return authorizationMetadata;
  }

  // --- Response models ---

  public record InitializeResult(
      String protocolVersion, Capabilities capabilities, ServerInfo serverInfo) {}

  public record Capabilities(
      ToolCapabilities tools,
      PromptCapabilities prompts,
      ResourceCapabilities resources,
      CatalogCapabilities catalog,
      AuthorizationCapabilities authorization) {}

  public record ToolCapabilities(boolean listChanged) {}

  public record PromptCapabilities(boolean listChanged) {}

  public record ResourceCapabilities(boolean listChanged, boolean read) {}

  public record CatalogCapabilities(boolean list, boolean search, String grouping) {}

  public record AuthorizationCapabilities(
      String mode, List<String> requiredScopes, OAuth2Capabilities oauth2) {}

  public record OAuth2Capabilities(boolean metadata, String resource, String wellKnownPath) {}

  public record ServerInfo(String name, String version) {}

  public record AuthorizationMetadata(
      String mode, List<String> requiredScopes, OAuthMetadata oauthMetadata) {

    public static final String RESOURCE_URI = "graphql-mcp://auth/metadata";
    public static final String WELL_KNOWN_PATH = ".well-known/oauth-authorization-server";

    public static AuthorizationMetadata none() {
      return new AuthorizationMetadata("none", List.of(), OAuthMetadata.defaults());
    }

    public boolean enabled() {
      return !"none".equalsIgnoreCase(mode)
          || !requiredScopes.isEmpty()
          || oauthMetadata.isConfigured();
    }
  }

  public record OAuthMetadata(
      String issuer,
      String authorizationEndpoint,
      String tokenEndpoint,
      String registrationEndpoint,
      String jwksUri,
      String serviceDocumentation,
      List<String> responseTypesSupported,
      List<String> grantTypesSupported,
      List<String> tokenEndpointAuthMethodsSupported) {

    public static OAuthMetadata defaults() {
      return new OAuthMetadata(
          null,
          null,
          null,
          null,
          null,
          null,
          List.of("code"),
          List.of("authorization_code", "refresh_token"),
          List.of("none"));
    }

    public boolean isConfigured() {
      return isNotBlank(issuer)
          || isNotBlank(authorizationEndpoint)
          || isNotBlank(tokenEndpoint)
          || isNotBlank(registrationEndpoint)
          || isNotBlank(jwksUri)
          || isNotBlank(serviceDocumentation);
    }

    private static boolean isNotBlank(String value) {
      return value != null && !value.isBlank();
    }
  }
}
