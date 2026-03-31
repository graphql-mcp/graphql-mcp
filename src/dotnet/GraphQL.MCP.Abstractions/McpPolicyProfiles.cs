namespace GraphQL.MCP.Abstractions;

/// <summary>
/// Built-in policy presets for common graphql-mcp publishing strategies.
/// </summary>
public enum McpPolicyPreset
{
    /// <summary>
    /// General-purpose defaults that balance safety and breadth.
    /// </summary>
    Balanced,

    /// <summary>
    /// Stricter publication rules for curated or external-facing servers.
    /// </summary>
    Strict,

    /// <summary>
    /// A tighter-but-practical baseline for curated internal catalogs.
    /// </summary>
    Curated,

    /// <summary>
    /// Broader publication limits for discovery-heavy internal exploration.
    /// </summary>
    Exploratory
}

/// <summary>
/// Built-in higher-level profile packs for common schema families and industry domains.
/// </summary>
public enum McpPolicyPack
{
    None,
    Commerce,
    Content,
    Operations
}

/// <summary>
/// Optional reusable policy override layer applied on top of a preset.
/// </summary>
public sealed class McpPolicyProfile
{
    /// <summary>
    /// Optional display name for the profile in logs or docs.
    /// </summary>
    public string? Name { get; set; }

    public ToolNamingPolicy? NamingPolicy { get; set; }

    public bool? AllowMutations { get; set; }

    public bool? IncludeDescriptions { get; set; }

    public bool? RequireDescriptionsForPublishedTools { get; set; }

    public int? MinDescriptionLength { get; set; }

    public int? MaxOutputDepth { get; set; }

    public int? MaxToolCount { get; set; }

    public int? MaxArgumentCount { get; set; }

    public int? MaxArgumentComplexity { get; set; }

    public HashSet<string> ExcludedFields { get; set; } = [];

    public HashSet<string> IncludedFields { get; set; } = [];

    public HashSet<string> ExcludedTypes { get; set; } = [];

    public HashSet<string> IncludedDomains { get; set; } = [];

    public HashSet<string> ExcludedDomains { get; set; } = [];
}

/// <summary>
/// Resolves built-in policy presets and reusable profile overrides into effective options.
/// </summary>
public static class McpPolicyProfiles
{
    public static McpOptions Resolve(McpOptions options)
    {
        var resolved = CreatePreset(options.PolicyPreset);

        ApplyProfile(resolved, CreatePack(options.PolicyPack));
        ApplyProfile(resolved, options.PolicyProfile);
        ApplyExplicitOverrides(resolved, options);

        resolved.PolicyPreset = options.PolicyPreset;
        resolved.PolicyPack = options.PolicyPack;
        resolved.PolicyProfile = CloneProfile(options.PolicyProfile);
        resolved.ToolPrefix = options.ToolPrefix;
        resolved.Authorization = CloneAuthorization(options.Authorization);
        resolved.Transport = options.Transport;

        return resolved;
    }

    public static McpOptions CreatePreset(McpPolicyPreset preset)
    {
        var options = new McpOptions();

        switch (preset)
        {
            case McpPolicyPreset.Strict:
                options.RequireDescriptionsForPublishedTools = true;
                options.MinDescriptionLength = 24;
                options.MaxOutputDepth = 2;
                options.MaxToolCount = 25;
                options.MaxArgumentCount = 10;
                options.MaxArgumentComplexity = 30;
                break;

            case McpPolicyPreset.Curated:
                options.RequireDescriptionsForPublishedTools = true;
                options.MinDescriptionLength = 12;
                options.MaxToolCount = 40;
                options.MaxArgumentCount = 15;
                options.MaxArgumentComplexity = 50;
                break;

            case McpPolicyPreset.Exploratory:
                options.MaxOutputDepth = 4;
                options.MaxToolCount = 100;
                options.MaxArgumentCount = 40;
                options.MaxArgumentComplexity = 120;
                break;

            case McpPolicyPreset.Balanced:
            default:
                break;
        }

        return options;
    }

    public static McpPolicyProfile? CreatePack(McpPolicyPack pack) =>
        pack switch
        {
            McpPolicyPack.Commerce => new McpPolicyProfile
            {
                Name = "commerce",
                IncludedDomains =
                [
                    "catalog", "product", "inventory", "order", "invoice", "payment",
                    "customer", "shipment"
                ],
                ExcludedDomains = ["admin", "internal"],
                MinDescriptionLength = 12,
                MaxArgumentComplexity = 60,
                MaxToolCount = 60
            },
            McpPolicyPack.Content => new McpPolicyProfile
            {
                Name = "content",
                IncludedDomains = ["article", "author", "content", "media", "asset", "category", "tag", "page"],
                ExcludedDomains = ["admin", "internal"],
                MinDescriptionLength = 8,
                MaxArgumentComplexity = 55,
                MaxToolCount = 75
            },
            McpPolicyPack.Operations => new McpPolicyProfile
            {
                Name = "operations",
                IncludedDomains =
                [
                    "service", "incident", "alert", "deployment", "ticket", "runbook",
                    "environment", "metric"
                ],
                ExcludedDomains = ["admin", "internal"],
                RequireDescriptionsForPublishedTools = true,
                MinDescriptionLength = 12,
                MaxArgumentComplexity = 45,
                MaxToolCount = 40
            },
            _ => null
        };

    private static void ApplyProfile(McpOptions target, McpPolicyProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        if (profile.NamingPolicy.HasValue)
        {
            target.NamingPolicy = profile.NamingPolicy.Value;
        }

        if (profile.AllowMutations.HasValue)
        {
            target.AllowMutations = profile.AllowMutations.Value;
        }

        if (profile.IncludeDescriptions.HasValue)
        {
            target.IncludeDescriptions = profile.IncludeDescriptions.Value;
        }

        if (profile.RequireDescriptionsForPublishedTools.HasValue)
        {
            target.RequireDescriptionsForPublishedTools = profile.RequireDescriptionsForPublishedTools.Value;
        }

        if (profile.MinDescriptionLength.HasValue)
        {
            target.MinDescriptionLength = profile.MinDescriptionLength.Value;
        }

        if (profile.MaxOutputDepth.HasValue)
        {
            target.MaxOutputDepth = profile.MaxOutputDepth.Value;
        }

        if (profile.MaxToolCount.HasValue)
        {
            target.MaxToolCount = profile.MaxToolCount.Value;
        }

        if (profile.MaxArgumentCount.HasValue)
        {
            target.MaxArgumentCount = profile.MaxArgumentCount.Value;
        }

        if (profile.MaxArgumentComplexity.HasValue)
        {
            target.MaxArgumentComplexity = profile.MaxArgumentComplexity.Value;
        }

        if (profile.ExcludedFields.Count > 0)
        {
            target.ExcludedFields = [.. profile.ExcludedFields];
        }

        if (profile.IncludedFields.Count > 0)
        {
            target.IncludedFields = [.. profile.IncludedFields];
        }

        if (profile.ExcludedTypes.Count > 0)
        {
            target.ExcludedTypes = [.. profile.ExcludedTypes];
        }

        if (profile.IncludedDomains.Count > 0)
        {
            target.IncludedDomains = [.. profile.IncludedDomains];
        }

        if (profile.ExcludedDomains.Count > 0)
        {
            target.ExcludedDomains = [.. profile.ExcludedDomains];
        }
    }

    private static void ApplyExplicitOverrides(McpOptions target, McpOptions source)
    {
        var defaults = new McpOptions();

        if (source.NamingPolicy != defaults.NamingPolicy)
        {
            target.NamingPolicy = source.NamingPolicy;
        }

        if (source.AllowMutations != defaults.AllowMutations)
        {
            target.AllowMutations = source.AllowMutations;
        }

        if (source.IncludeDescriptions != defaults.IncludeDescriptions)
        {
            target.IncludeDescriptions = source.IncludeDescriptions;
        }

        if (source.RequireDescriptionsForPublishedTools != defaults.RequireDescriptionsForPublishedTools)
        {
            target.RequireDescriptionsForPublishedTools = source.RequireDescriptionsForPublishedTools;
        }

        if (source.MinDescriptionLength != defaults.MinDescriptionLength)
        {
            target.MinDescriptionLength = source.MinDescriptionLength;
        }

        if (source.MaxOutputDepth != defaults.MaxOutputDepth)
        {
            target.MaxOutputDepth = source.MaxOutputDepth;
        }

        if (source.MaxToolCount != defaults.MaxToolCount)
        {
            target.MaxToolCount = source.MaxToolCount;
        }

        if (source.MaxArgumentCount != defaults.MaxArgumentCount)
        {
            target.MaxArgumentCount = source.MaxArgumentCount;
        }

        if (source.MaxArgumentComplexity != defaults.MaxArgumentComplexity)
        {
            target.MaxArgumentComplexity = source.MaxArgumentComplexity;
        }

        if (source.ExcludedFields.Count > 0)
        {
            target.ExcludedFields = [.. source.ExcludedFields];
        }

        if (source.IncludedFields.Count > 0)
        {
            target.IncludedFields = [.. source.IncludedFields];
        }

        if (source.ExcludedTypes.Count > 0)
        {
            target.ExcludedTypes = [.. source.ExcludedTypes];
        }

        if (source.IncludedDomains.Count > 0)
        {
            target.IncludedDomains = [.. source.IncludedDomains];
        }

        if (source.ExcludedDomains.Count > 0)
        {
            target.ExcludedDomains = [.. source.ExcludedDomains];
        }
    }

    private static McpAuthorizationOptions CloneAuthorization(McpAuthorizationOptions authorization) =>
        new()
        {
            Mode = authorization.Mode,
            RequiredScopes = [.. authorization.RequiredScopes],
            Metadata = CloneAuthorizationMetadata(authorization.Metadata)
        };

    private static McpOAuthMetadataOptions CloneAuthorizationMetadata(McpOAuthMetadataOptions metadata) =>
        new()
        {
            Issuer = metadata.Issuer,
            AuthorizationEndpoint = metadata.AuthorizationEndpoint,
            TokenEndpoint = metadata.TokenEndpoint,
            RegistrationEndpoint = metadata.RegistrationEndpoint,
            JwksUri = metadata.JwksUri,
            ServiceDocumentation = metadata.ServiceDocumentation,
            ResponseTypesSupported = [.. metadata.ResponseTypesSupported],
            GrantTypesSupported = [.. metadata.GrantTypesSupported],
            TokenEndpointAuthMethodsSupported = [.. metadata.TokenEndpointAuthMethodsSupported]
        };

    private static McpPolicyProfile? CloneProfile(McpPolicyProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return new McpPolicyProfile
        {
            Name = profile.Name,
            NamingPolicy = profile.NamingPolicy,
            AllowMutations = profile.AllowMutations,
            IncludeDescriptions = profile.IncludeDescriptions,
            RequireDescriptionsForPublishedTools = profile.RequireDescriptionsForPublishedTools,
            MinDescriptionLength = profile.MinDescriptionLength,
            MaxOutputDepth = profile.MaxOutputDepth,
            MaxToolCount = profile.MaxToolCount,
            MaxArgumentCount = profile.MaxArgumentCount,
            MaxArgumentComplexity = profile.MaxArgumentComplexity,
            ExcludedFields = [.. profile.ExcludedFields],
            IncludedFields = [.. profile.IncludedFields],
            ExcludedTypes = [.. profile.ExcludedTypes],
            IncludedDomains = [.. profile.IncludedDomains],
            ExcludedDomains = [.. profile.ExcludedDomains]
        };
    }
}
