namespace GraphQL.MCP.Abstractions;

/// <summary>
/// Primary configuration for graphql-mcp.
/// </summary>
public sealed class McpOptions
{
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
    StreamableHttp
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
