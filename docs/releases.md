# Releases

## Current Posture

- The repository is prepared for stable `.NET` and `Java` release lines.
- Existing prerelease tags remain valid for early adopters, but stable tags without a prerelease suffix become the normal latest release line.
- Spring GraphQL is already published, and the Netflix DGS adapter is part of the stable-ready Java surface for the next stable Java tag.

## Stable Release Criteria

Use these rules before cutting a stable release:

1. no breaking changes across `tools/list`, `catalog/*`, `resources/*`, `prompts/*`, or auth metadata discovery for one quiet cycle
2. CI stays green for packaging, tests, formatting, and example smoke builds
3. examples and docs match the actual shipped transport and discovery surface
4. publish workflows create prereleases only for prerelease tags, and stable tags create the normal latest GitHub release line

## Stable .NET Checklist

- full solution build and tests green in CI
- package creation validated in CI
- no expected schema-shape or transport-surface changes queued for the immediate next release
- docs and examples reflect both `StreamableHttp` and `Stdio`
- discovery, resources, prompts, and auth metadata remain unchanged for one quiet cycle

## Stable Java Checklist

- reactor tests green
- formatting clean
- Spring GraphQL and DGS example builds green
- release-profile verification green
- Spring GraphQL and DGS both validated through CI and example coverage
- discovery, resources, prompts, and auth metadata remain unchanged for one quiet cycle

## Tag Guidance

- use prerelease tags such as `dotnet-v0.1.0-alpha.6` or `java-v0.1.0-rc.1` when you want GitHub and package feeds to mark the release as prerelease
- use stable tags such as `dotnet-v0.1.0` or `java-v0.1.0` when you want the workflows to create the normal latest release line
- keep `.NET` and `Java` version numbers aligned when the public discovery and policy surface is the same across both runtimes
