# Releases

## Current Channels

- `.NET`: alpha line, approaching beta
- `Java / Spring GraphQL`: alpha preview
- `Java / Netflix DGS`: implemented in the repo, queued for the next alpha

## Stable Release Criteria

Use these rules before cutting a stable release:

1. no breaking changes across `tools/list`, `catalog/*`, `resources/*`, `prompts/*`, or auth metadata discovery for one quiet cycle
2. CI stays green for packaging, tests, formatting, and example smoke builds
3. examples and docs match the actual shipped transport and discovery surface
4. publish workflows create prereleases only for prerelease tags

## .NET Beta Checklist

- full solution build and tests green in CI
- package creation validated in CI
- no expected schema-shape or transport-surface changes queued for the immediate next release
- docs and examples reflect `StreamableHttp` and `Stdio`

## Java Beta Checklist

- reactor tests green
- formatting clean
- example build green
- release-profile verification green
- Spring GraphQL and DGS both validated through CI and example coverage
- one quiet hardening cycle after the second adapter lands

## Version Guidance

- use `alpha` while discovery surfaces, policy packs, or transports are still changing rapidly
- move to `beta` when the public shape is mostly stable and the work is shifting toward compatibility and polish
- use a stable `0.1.0` only after a quiet beta cycle with no major metadata or transport churn
