# 21. Pin the .NET SDK to an exact version; patches arrive as reviewed bumps

- Status: Accepted
- Date: 2026-11-11

## Context

[ADR 2](0002-dotnet-toolchain.md) pinned the SDK in `global.json` with
`rollForward: latestPatch`, and restores in locked mode against NuGet lock
files. `actions/setup-dotnet` reads that policy and installs the newest patch
of the feature band, so CI's SDK changed whenever Microsoft shipped one.

The SDK adds implicit package references whose versions follow its bundled
runtime, such as `Microsoft.NET.ILLink.Tasks` for the trimmed Extension, and
these are recorded in the lock files. On 11 November 2026 CI picked up SDK
10.0.404, which asks for ILLink 10.0.15, while the lock files generated with
10.0.403 say 10.0.14. Locked restore failed on every branch, without any
change in the repository.

## Decision

- `global.json` pins the SDK with `rollForward: patch`. `setup-dotnet` then
  installs exactly that version, and the SDK used in CI changes only in a
  commit.
- An SDK update is a pull request that changes the version in `global.json`
  and regenerates the lock files with `dotnet restore --force-evaluate` in the
  same commit. Renovate opens these; one whose lock files do not match fails
  CI and is completed by hand.
- The rest of ADR 2 stands.

## Alternatives considered

- **Keep `latestPatch` and bump the version and lock files when it breaks**:
  fixes the build until the next patch Tuesday, then breaks every branch again.
- **`rollForward: disable`**: also exact in CI, but refuses to build locally
  without that precise SDK installed. `patch` gives the same result in CI.
- **Leave the lock files out of locked mode for SDK-implicit packages**:
  weakens the reproducible build ADR 2 asks for; locked mode would no longer
  cover every package in the build.

## Consequences

- CI builds are reproducible: the same commit restores the same packages.
- The Extension publishes self-contained, so the .NET runtime in the MSIX is
  the one bundled with the pinned SDK. Runtime security fixes reach users only
  when the SDK bump is merged and released, so SDK bump pull requests should
  not be left waiting.
- A developer without the pinned SDK installed rolls forward to a newer patch
  locally, and locked restore fails until they install the pinned version.
