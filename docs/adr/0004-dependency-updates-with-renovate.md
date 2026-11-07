# 4. Renovate for dependency updates

- Status: Accepted
- Date: 2026-11-07

## Context

Pinned versions only stay safe if they are updated. The project pins NuGet
packages, the .NET SDK, dotnet tools, GitHub Actions by SHA, pre-commit hooks
and tool versions set in workflow variables.

## Decision

Use the hosted Mend Renovate app, running on Mondays, with OSV vulnerability
alerts and a three-day minimum release age. Minor, patch, pin and digest
updates merge automatically once CI is green; majors and new .NET SDK feature
bands wait for review. Lock file maintenance runs weekly. Dependabot is used
for alerts only.

## Alternatives considered

- **Dependabot version updates**: no grouping across managers, no support for
  versions in workflow variables, and no lock file maintenance for NuGet.
- **Manual updates**: would not happen often enough.

## Consequences

- Updates arrive in one weekly batch instead of a steady stream.
- Automerge relies on the pipeline being a real gate, which is another reason
  to keep its checks strict.
- The three-day delay gives the ecosystem time to catch a compromised release.
