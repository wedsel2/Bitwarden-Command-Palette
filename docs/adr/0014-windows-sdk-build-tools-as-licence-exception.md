# 14. Microsoft's Windows SDK build tools are the one exception to the open-source rule

- Status: Accepted
- Date: 2026-11-07

## Context

[ADR 3](0003-security-and-quality-scanning.md) lets CI fail on any package
without an OSI licence. A Command Palette extension must be an MSIX package, and
building one needs `Microsoft.Windows.SDK.BuildTools.MSIX`, which is under the
proprietary Microsoft Windows SDK licence. The CsWinRT projection of Command
Palette's interfaces also needs the Windows SDK's metadata on the build machine.
Both run only at build time; nothing from them is in the extension's output.
Everything that ships (Command Palette SDK, CsWinRT runtime, the COM server
helper, .NET) is MIT.

## Decision

- The open-source rule covers **everything that ships in the extension and every
  third-party build or test tool**.
- **Microsoft's Windows SDK build tools are the single named exception**: the
  `Microsoft.Windows.SDK.BuildTools.MSIX` package is listed in
  `license-exceptions.json`, and the Windows SDK itself is installed on the
  build machine (preinstalled on GitHub's Windows runners).
- Adding anything else to `license-exceptions.json` needs a new record.
- A package whose licence is open source but stored in a form the checker cannot
  read (CsWinRT's MIT licence file) goes in `license-overrides.json`, pinned to
  the exact version that was read, so every update is read again.

## Alternatives considered

- **Building MSIX with `microsoft/msix-packaging` (MIT) compiled from source**:
  removes the package, but adds a native build to the pipeline, and signing and
  the CsWinRT projection still need Windows SDK tools.
- **No exception, no MSIX**: Command Palette loads extensions only from packages.

## Consequences

- The licence check stays a hard gate for everything else.
- Building requires Windows with the Windows SDK; contributors install it once.
- A Renovate update of CsWinRT fails the licence check until its licence file is
  read and the override's version is updated.
