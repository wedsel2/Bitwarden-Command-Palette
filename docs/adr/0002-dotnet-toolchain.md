# 2. .NET toolchain: pinned SDK, central packages, lock files, analyzers, xUnit v3

- Status: Accepted; SDK roll-forward superseded by [21](0021-pin-the-dotnet-sdk-exactly.md)
- Date: 2026-11-07

## Context

Command Palette extensions are written in C# against WinRT interfaces and
packaged as MSIX, so the language is set by the platform. The toolchain has to
make every build reproducible and every dependency visible.

## Decision

- **.NET 10 (LTS)**, SDK version pinned in `global.json` with `latestPatch`.
- **Central Package Management**: versions only in `Directory.Packages.props`,
  with transitive pinning.
- **NuGet lock files** for every project, restored in locked mode in CI;
  nuget.org as the only source through `nuget.config`.
- **Analyzers**: `AnalysisLevel` `latest-all`, code style enforced in build,
  warnings as errors, nullable reference types on.
- **Tests**: xUnit v3 on Microsoft Testing Platform, coverage by coverlet.MTP
  with an 80% line floor.
- **Deterministic builds** with `ContinuousIntegrationBuild` in CI.
- Shared settings live in `Directory.Build.props`.

## Alternatives considered

- **Python or Rust**: the original notes weighed them, but Command Palette
  extensions are C#; anything else would need its own bridge to WinRT.
- **NUnit or MSTest**: both fine; xUnit v3 runs on the new test platform
  natively and is the most common choice in open-source .NET.
- **VSTest with coverlet.msbuild**: not supported for xUnit v3 on .NET 10.

## Consequences

- Adding a package means a version in `Directory.Packages.props`, a
  `dotnet restore` to update the lock files, and both files in the commit.
- New analyzer rules arrive with SDK updates and can break the build; Renovate
  holds SDK feature-band updates for review.
- The Windows target framework means build and tests run on Windows runners.
