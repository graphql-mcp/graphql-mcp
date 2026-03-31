package dev.graphqlmcp.autoconfigure;

import static org.junit.jupiter.api.Assertions.*;

import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper;
import dev.graphqlmcp.mapping.GraphQLToMCPToolMapper.GraphQLMCPConfig;
import dev.graphqlmcp.properties.GraphQLMCPProperties;
import java.util.List;
import org.junit.jupiter.api.Test;

class GraphQLMCPPolicyProfilesTest {

  @Test
  void resolves_strict_policy_preset() {
    GraphQLMCPProperties properties = new GraphQLMCPProperties();
    properties.setPolicyPreset("strict");

    GraphQLMCPConfig config = GraphQLMCPPolicyProfiles.resolve(properties);

    assertAll(
        () -> assertTrue(config.requireDescriptions()),
        () -> assertEquals(24, config.minDescriptionLength()),
        () -> assertEquals(2, config.maxOutputDepth()),
        () -> assertEquals(25, config.maxToolCount()),
        () -> assertEquals(10, config.maxArgumentCount()),
        () -> assertEquals(30, config.maxArgumentComplexity()));
  }

  @Test
  void applies_policy_profile_overrides_on_top_of_preset() {
    GraphQLMCPProperties properties = new GraphQLMCPProperties();
    properties.setPolicyPreset("strict");
    properties.getPolicyProfile().setRequireDescriptions(false);
    properties.getPolicyProfile().setMinDescriptionLength(0);
    properties.getPolicyProfile().setIncludedDomains(List.of("book"));
    properties.getPolicyProfile().setNamingPolicy("raw");

    GraphQLMCPConfig config = GraphQLMCPPolicyProfiles.resolve(properties);

    assertAll(
        () -> assertFalse(config.requireDescriptions()),
        () -> assertEquals(0, config.minDescriptionLength()),
        () -> assertEquals(GraphQLToMCPToolMapper.NamingPolicy.RAW, config.namingPolicy()),
        () -> assertEquals(1, config.includedDomains().size()),
        () -> assertTrue(config.includedDomains().contains("book")));
  }

  @Test
  void applies_explicit_top_level_overrides_after_policy_preset() {
    GraphQLMCPProperties properties = new GraphQLMCPProperties();
    properties.setPolicyPreset("exploratory");
    properties.setMaxArgumentCount(1);
    properties.setExcludedFields(List.of("secretNote"));

    GraphQLMCPConfig config = GraphQLMCPPolicyProfiles.resolve(properties);

    assertAll(
        () -> assertEquals(1, config.maxArgumentCount()),
        () -> assertEquals(1, config.excludedFields().size()),
        () -> assertTrue(config.excludedFields().contains("secretNote")),
        () -> assertEquals(100, config.maxToolCount()));
  }
}
