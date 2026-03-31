package dev.graphqlmcp.autoconfigure;

import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper;
import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper.GraphQLMCPConfig;
import dev.graphqlmcp.properties.GraphQLMCPProperties;
import java.util.Set;

/** Resolves built-in policy presets and optional reusable profile overrides. */
final class GraphQLMCPPolicyProfiles {

  private GraphQLMCPPolicyProfiles() {}

  static GraphQLMCPConfig resolve(GraphQLMCPProperties properties) {
    GraphQLMCPProperties defaults = new GraphQLMCPProperties();
    GraphQLMCPConfig config = preset(properties.getPolicyPreset());

    config = applyProfile(config, pack(properties.getPolicyPack()));
    config = applyProfile(config, properties.getPolicyProfile());
    config = applyExplicitOverrides(config, properties, defaults);

    return new GraphQLMCPConfig(
        properties.getToolPrefix(),
        config.namingPolicy(),
        config.allowMutations(),
        config.excludedFields(),
        config.includedDomains(),
        config.excludedDomains(),
        config.maxOutputDepth(),
        config.maxToolCount(),
        config.requireDescriptions(),
        config.minDescriptionLength(),
        config.maxArgumentCount(),
        config.maxArgumentComplexity());
  }

  private static GraphQLMCPConfig preset(String presetName) {
    String preset = presetName == null ? "balanced" : presetName.trim().toLowerCase();

    return switch (preset) {
      case "strict" ->
          new GraphQLMCPConfig(
              null,
              GraphQLToMCPToolMapper.NamingPolicy.VERB_NOUN,
              false,
              Set.of(),
              Set.of(),
              Set.of(),
              2,
              25,
              true,
              24,
              10,
              30);
      case "curated" ->
          new GraphQLMCPConfig(
              null,
              GraphQLToMCPToolMapper.NamingPolicy.VERB_NOUN,
              false,
              Set.of(),
              Set.of(),
              Set.of(),
              3,
              40,
              true,
              12,
              15,
              50);
      case "exploratory" ->
          new GraphQLMCPConfig(
              null,
              GraphQLToMCPToolMapper.NamingPolicy.VERB_NOUN,
              false,
              Set.of(),
              Set.of(),
              Set.of(),
              4,
              100,
              false,
              0,
              40,
              120);
      default -> GraphQLMCPConfig.defaults();
    };
  }

  private static GraphQLMCPProperties.PolicyProfile pack(String packName) {
    String pack = packName == null ? "none" : packName.trim().toLowerCase();

    GraphQLMCPProperties.PolicyProfile profile = new GraphQLMCPProperties.PolicyProfile();
    switch (pack) {
      case "commerce" -> {
        profile.setName("commerce");
        profile.setIncludedDomains(
            java.util.List.of(
                "catalog",
                "product",
                "inventory",
                "order",
                "invoice",
                "payment",
                "customer",
                "shipment"));
        profile.setExcludedDomains(java.util.List.of("admin", "internal"));
        profile.setMinDescriptionLength(12);
        profile.setMaxArgumentComplexity(60);
        profile.setMaxToolCount(60);
        return profile;
      }
      case "content" -> {
        profile.setName("content");
        profile.setIncludedDomains(
            java.util.List.of(
                "article", "author", "content", "media", "asset", "category", "tag", "page"));
        profile.setExcludedDomains(java.util.List.of("admin", "internal"));
        profile.setMinDescriptionLength(8);
        profile.setMaxArgumentComplexity(55);
        profile.setMaxToolCount(75);
        return profile;
      }
      case "operations" -> {
        profile.setName("operations");
        profile.setIncludedDomains(
            java.util.List.of(
                "service",
                "incident",
                "alert",
                "deployment",
                "ticket",
                "runbook",
                "environment",
                "metric"));
        profile.setExcludedDomains(java.util.List.of("admin", "internal"));
        profile.setRequireDescriptions(true);
        profile.setMinDescriptionLength(12);
        profile.setMaxArgumentComplexity(45);
        profile.setMaxToolCount(40);
        return profile;
      }
      default -> {
        return null;
      }
    }
  }

  private static GraphQLMCPConfig applyProfile(
      GraphQLMCPConfig config, GraphQLMCPProperties.PolicyProfile profile) {
    if (profile == null) {
      return config;
    }

    return new GraphQLMCPConfig(
        config.toolPrefix(),
        profile.getNamingPolicy() == null
            ? config.namingPolicy()
            : mapNamingPolicy(profile.getNamingPolicy()),
        profile.getAllowMutations() == null ? config.allowMutations() : profile.getAllowMutations(),
        profile.getExcludedFields().isEmpty()
            ? config.excludedFields()
            : Set.copyOf(profile.getExcludedFields()),
        profile.getIncludedDomains().isEmpty()
            ? config.includedDomains()
            : Set.copyOf(profile.getIncludedDomains()),
        profile.getExcludedDomains().isEmpty()
            ? config.excludedDomains()
            : Set.copyOf(profile.getExcludedDomains()),
        profile.getMaxOutputDepth() == null ? config.maxOutputDepth() : profile.getMaxOutputDepth(),
        profile.getMaxToolCount() == null ? config.maxToolCount() : profile.getMaxToolCount(),
        profile.getRequireDescriptions() == null
            ? config.requireDescriptions()
            : profile.getRequireDescriptions(),
        profile.getMinDescriptionLength() == null
            ? config.minDescriptionLength()
            : profile.getMinDescriptionLength(),
        profile.getMaxArgumentCount() == null
            ? config.maxArgumentCount()
            : profile.getMaxArgumentCount(),
        profile.getMaxArgumentComplexity() == null
            ? config.maxArgumentComplexity()
            : profile.getMaxArgumentComplexity());
  }

  private static GraphQLMCPConfig applyExplicitOverrides(
      GraphQLMCPConfig config, GraphQLMCPProperties properties, GraphQLMCPProperties defaults) {
    return new GraphQLMCPConfig(
        null,
        properties.getNamingPolicy().equalsIgnoreCase(defaults.getNamingPolicy())
            ? config.namingPolicy()
            : mapNamingPolicy(properties.getNamingPolicy()),
        properties.isAllowMutations() == defaults.isAllowMutations()
            ? config.allowMutations()
            : properties.isAllowMutations(),
        properties.getExcludedFields().isEmpty()
            ? config.excludedFields()
            : Set.copyOf(properties.getExcludedFields()),
        properties.getIncludedDomains().isEmpty()
            ? config.includedDomains()
            : Set.copyOf(properties.getIncludedDomains()),
        properties.getExcludedDomains().isEmpty()
            ? config.excludedDomains()
            : Set.copyOf(properties.getExcludedDomains()),
        properties.getMaxOutputDepth() == defaults.getMaxOutputDepth()
            ? config.maxOutputDepth()
            : properties.getMaxOutputDepth(),
        properties.getMaxToolCount() == defaults.getMaxToolCount()
            ? config.maxToolCount()
            : properties.getMaxToolCount(),
        properties.isRequireDescriptions() == defaults.isRequireDescriptions()
            ? config.requireDescriptions()
            : properties.isRequireDescriptions(),
        properties.getMinDescriptionLength() == defaults.getMinDescriptionLength()
            ? config.minDescriptionLength()
            : properties.getMinDescriptionLength(),
        properties.getMaxArgumentCount() == defaults.getMaxArgumentCount()
            ? config.maxArgumentCount()
            : properties.getMaxArgumentCount(),
        properties.getMaxArgumentComplexity() == defaults.getMaxArgumentComplexity()
            ? config.maxArgumentComplexity()
            : properties.getMaxArgumentComplexity());
  }

  private static GraphQLToMCPToolMapper.NamingPolicy mapNamingPolicy(String policy) {
    if ("raw".equalsIgnoreCase(policy)) {
      return GraphQLToMCPToolMapper.NamingPolicy.RAW;
    }

    return GraphQLToMCPToolMapper.NamingPolicy.VERB_NOUN;
  }
}
