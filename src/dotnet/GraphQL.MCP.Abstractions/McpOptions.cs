namespace GraphQL.MCP.Abstractions;

/// <summary>
/// Primary configuration for graphql-mcp.
/// </summary>
public sealed class McpOptions
{
    /// <summary>
    /// Built-in policy preset applied before profile and explicit overrides.
    /// Default: Balanced.
    /// </summary>
    public McpPolicyPreset PolicyPreset { get; set; } = McpPolicyPreset.Balanced;

    /// <summary>
    /// Optional reusable policy profile applied on top of the selected preset.
    /// </summary>
    public McpPolicyProfile? PolicyProfile { get; set; }

    /// <summary>
    /// Optional shared policy pack for common schema families and industry domains.
    /// Applied after the preset and before PolicyProfile overrides.
    /// </summary>
    public McpPolicyPack PolicyPack { get; set; }

    /// <summary>
    /// Prefix for all generated tool names. Example: "myapi" → "myapi_getUsers".
    /// </summary>
    public string? ToolPrefix { get; set; }

    /// <summary>
    /// Maximum depth for auto-generated GraphQL selection sets. Default: 3.
    /// </summary>
    public int MaxOutputDepth { get; set; } = 3;

    /// <summary>
    /// Maximum number of tools to publish. Default: 50.
    /// </summary>
    public int MaxToolCount { get; set; } = 50;

    /// <summary>
    /// Maximum number of arguments allowed on a published operation. Default: 25.
    /// Operations with more arguments are skipped to avoid overly complex tools.
    /// </summary>
    public int MaxArgumentCount { get; set; } = 25;

    /// <summary>
    /// Maximum weighted argument complexity allowed on a published operation. Default: 75.
    /// Nested input objects, lists, and non-null wrappers increase the score.
    /// </summary>
    public int MaxArgumentComplexity { get; set; } = 75;

    /// <summary>
    /// Naming policy for generated tool names.
    /// </summary>
    public ToolNamingPolicy NamingPolicy { get; set; } = ToolNamingPolicy.VerbNoun;

    /// <summary>
    /// Whether mutation operations are published as tools. Default: false.
    /// </summary>
    public bool AllowMutations { get; set; }

    /// <summary>
    /// Whether to include GraphQL descriptions in tool descriptors.
    /// </summary>
    public bool IncludeDescriptions { get; set; } = true;

    /// <summary>
    /// Whether operations without descriptions should be skipped entirely.
    /// Default: false.
    /// </summary>
    public bool RequireDescriptionsForPublishedTools { get; set; }

    /// <summary>
    /// Minimum non-whitespace description length required when descriptions are present or required.
    /// Default: 0 (no length gate).
    /// </summary>
    public int MinDescriptionLength { get; set; }

    /// <summary>
    /// GraphQL field names to exclude from tool generation.
    /// Supports glob patterns: "*password*", "internal_*", "*.secret".
    /// </summary>
    public HashSet<string> ExcludedFields { get; set; } = [];

    /// <summary>
    /// GraphQL field names to include (allowlist). When non-empty, only fields matching
    /// this list are considered. Supports glob patterns.
    /// </summary>
    public HashSet<string> IncludedFields { get; set; } = [];

    /// <summary>
    /// GraphQL type names to exclude. Operations returning/accepting these types are skipped.
    /// </summary>
    public HashSet<string> ExcludedTypes { get; set; } = [];

    /// <summary>
    /// Domain names to include. When non-empty, only operations in matching inferred domains are published.
    /// </summary>
    public HashSet<string> IncludedDomains { get; set; } = [];

    /// <summary>
    /// Domain names to exclude from publication.
    /// </summary>
    public HashSet<string> ExcludedDomains { get; set; } = [];

    /// <summary>
    /// Authorization configuration.
    /// </summary>
    public McpAuthorizationOptions Authorization { get; set; } = new();

    /// <summary>
    /// Transport mode for the MCP server endpoint.
    /// </summary>
    public McpTransport Transport { get; set; } = McpTransport.StreamableHttp;
}

/// <summary>
/// Authorization configuration for MCP.
/// </summary>
public sealed class McpAuthorizationOptions
{
    /// <summary>
    /// How authorization is handled. Default: None.
    /// </summary>
    public McpAuthMode Mode { get; set; } = McpAuthMode.None;

    /// <summary>
    /// Required scopes for tool invocation (when Mode != None).
    /// </summary>
    public List<string> RequiredScopes { get; set; } = [];

    /// <summary>
    /// Optional OAuth 2.1-style metadata advertised to MCP clients.
    /// </summary>
    public McpOAuthMetadataOptions Metadata { get; set; } = new();
}

/// <summary>
/// Optional OAuth metadata advertised through MCP resources and well-known metadata routes.
/// </summary>
public sealed class McpOAuthMetadataOptions
{
    /// <summary>
    /// Authorization server issuer identifier.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// OAuth authorization endpoint used by interactive clients.
    /// </summary>
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>
    /// OAuth token endpoint used by clients to exchange tokens.
    /// </summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>
    /// Optional OAuth client registration endpoint.
    /// </summary>
    public string? RegistrationEndpoint { get; set; }

    /// <summary>
    /// Optional JWKS URI for token verification metadata.
    /// </summary>
    public string? JwksUri { get; set; }

    /// <summary>
    /// Optional documentation URL describing the server's auth expectations.
    /// </summary>
    public string? ServiceDocumentation { get; set; }

    /// <summary>
    /// Supported OAuth response types. Default: ["code"].
    /// </summary>
    public List<string> ResponseTypesSupported { get; set; } = ["code"];

    /// <summary>
    /// Supported OAuth grant types. Default: ["authorization_code", "refresh_token"].
    /// </summary>
    public List<string> GrantTypesSupported { get; set; } = ["authorization_code", "refresh_token"];

    /// <summary>
    /// Supported token endpoint auth methods. Default: ["none"].
    /// </summary>
    public List<string> TokenEndpointAuthMethodsSupported { get; set; } = ["none"];
}

/// <summary>
/// Authorization mode for MCP endpoints.
/// </summary>
public enum McpAuthMode
{
    /// <summary>No authorization.</summary>
    None,

    /// <summary>Forward the caller's auth token to the GraphQL executor.</summary>
    Passthrough
}

/// <summary>
/// MCP transport mode.
/// </summary>
public enum McpTransport
{
    /// <summary>Streamable HTTP (MCP spec 2025-06-18).</summary>
    StreamableHttp,

    /// <summary>stdio transport for local/embedded MCP clients.</summary>
    Stdio
}

/// <summary>
/// Policy for generating tool names from GraphQL field names.
/// </summary>
public enum ToolNamingPolicy
{
    /// <summary>Prefix query fields with "get_". Mutations use field name as verb. Default.</summary>
    VerbNoun,

    /// <summary>Use GraphQL field name as-is.</summary>
    Raw,

    /// <summary>Use GraphQL field name prefixed with ToolPrefix only.</summary>
    PrefixedRaw
}
