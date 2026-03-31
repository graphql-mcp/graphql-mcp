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
  private String policyPreset = "balanced";
  private String policyPack = "none";
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
  private PolicyProfile policyProfile = new PolicyProfile();

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

  public String getPolicyPreset() {
    return policyPreset;
  }

  public void setPolicyPreset(String policyPreset) {
    this.policyPreset = policyPreset;
  }

  public String getPolicyPack() {
    return policyPack;
  }

  public void setPolicyPack(String policyPack) {
    this.policyPack = policyPack;
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

  public PolicyProfile getPolicyProfile() {
    return policyProfile;
  }

  public void setPolicyProfile(PolicyProfile policyProfile) {
    this.policyProfile = policyProfile;
  }

  public static class Authorization {
    private String mode = "none";
    private List<String> requiredScopes = new ArrayList<>();
    private Metadata metadata = new Metadata();

    public String getMode() {
      return mode;
    }

    public void setMode(String mode) {
      this.mode = mode;
    }

    public List<String> getRequiredScopes() {
      return requiredScopes;
    }

    public void setRequiredScopes(List<String> requiredScopes) {
      this.requiredScopes = requiredScopes;
    }

    public Metadata getMetadata() {
      return metadata;
    }

    public void setMetadata(Metadata metadata) {
      this.metadata = metadata;
    }
  }

  public static class Metadata {
    private String issuer;
    private String authorizationEndpoint;
    private String tokenEndpoint;
    private String registrationEndpoint;
    private String jwksUri;
    private String serviceDocumentation;
    private List<String> responseTypesSupported = new ArrayList<>(List.of("code"));
    private List<String> grantTypesSupported =
        new ArrayList<>(List.of("authorization_code", "refresh_token"));
    private List<String> tokenEndpointAuthMethodsSupported = new ArrayList<>(List.of("none"));

    public String getIssuer() {
      return issuer;
    }

    public void setIssuer(String issuer) {
      this.issuer = issuer;
    }

    public String getAuthorizationEndpoint() {
      return authorizationEndpoint;
    }

    public void setAuthorizationEndpoint(String authorizationEndpoint) {
      this.authorizationEndpoint = authorizationEndpoint;
    }

    public String getTokenEndpoint() {
      return tokenEndpoint;
    }

    public void setTokenEndpoint(String tokenEndpoint) {
      this.tokenEndpoint = tokenEndpoint;
    }

    public String getRegistrationEndpoint() {
      return registrationEndpoint;
    }

    public void setRegistrationEndpoint(String registrationEndpoint) {
      this.registrationEndpoint = registrationEndpoint;
    }

    public String getJwksUri() {
      return jwksUri;
    }

    public void setJwksUri(String jwksUri) {
      this.jwksUri = jwksUri;
    }

    public String getServiceDocumentation() {
      return serviceDocumentation;
    }

    public void setServiceDocumentation(String serviceDocumentation) {
      this.serviceDocumentation = serviceDocumentation;
    }

    public List<String> getResponseTypesSupported() {
      return responseTypesSupported;
    }

    public void setResponseTypesSupported(List<String> responseTypesSupported) {
      this.responseTypesSupported = responseTypesSupported;
    }

    public List<String> getGrantTypesSupported() {
      return grantTypesSupported;
    }

    public void setGrantTypesSupported(List<String> grantTypesSupported) {
      this.grantTypesSupported = grantTypesSupported;
    }

    public List<String> getTokenEndpointAuthMethodsSupported() {
      return tokenEndpointAuthMethodsSupported;
    }

    public void setTokenEndpointAuthMethodsSupported(
        List<String> tokenEndpointAuthMethodsSupported) {
      this.tokenEndpointAuthMethodsSupported = tokenEndpointAuthMethodsSupported;
    }
  }

  public static class PolicyProfile {
    private String name;
    private String namingPolicy;
    private Boolean allowMutations;
    private Boolean requireDescriptions;
    private Integer minDescriptionLength;
    private Integer maxOutputDepth;
    private Integer maxToolCount;
    private Integer maxArgumentCount;
    private Integer maxArgumentComplexity;
    private List<String> excludedFields = new ArrayList<>();
    private List<String> includedDomains = new ArrayList<>();
    private List<String> excludedDomains = new ArrayList<>();

    public String getName() {
      return name;
    }

    public void setName(String name) {
      this.name = name;
    }

    public String getNamingPolicy() {
      return namingPolicy;
    }

    public void setNamingPolicy(String namingPolicy) {
      this.namingPolicy = namingPolicy;
    }

    public Boolean getAllowMutations() {
      return allowMutations;
    }

    public void setAllowMutations(Boolean allowMutations) {
      this.allowMutations = allowMutations;
    }

    public Boolean getRequireDescriptions() {
      return requireDescriptions;
    }

    public void setRequireDescriptions(Boolean requireDescriptions) {
      this.requireDescriptions = requireDescriptions;
    }

    public Integer getMinDescriptionLength() {
      return minDescriptionLength;
    }

    public void setMinDescriptionLength(Integer minDescriptionLength) {
      this.minDescriptionLength = minDescriptionLength;
    }

    public Integer getMaxOutputDepth() {
      return maxOutputDepth;
    }

    public void setMaxOutputDepth(Integer maxOutputDepth) {
      this.maxOutputDepth = maxOutputDepth;
    }

    public Integer getMaxToolCount() {
      return maxToolCount;
    }

    public void setMaxToolCount(Integer maxToolCount) {
      this.maxToolCount = maxToolCount;
    }

    public Integer getMaxArgumentCount() {
      return maxArgumentCount;
    }

    public void setMaxArgumentCount(Integer maxArgumentCount) {
      this.maxArgumentCount = maxArgumentCount;
    }

    public Integer getMaxArgumentComplexity() {
      return maxArgumentComplexity;
    }

    public void setMaxArgumentComplexity(Integer maxArgumentComplexity) {
      this.maxArgumentComplexity = maxArgumentComplexity;
    }

    public List<String> getExcludedFields() {
      return excludedFields;
    }

    public void setExcludedFields(List<String> excludedFields) {
      this.excludedFields = excludedFields;
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
  }
}
