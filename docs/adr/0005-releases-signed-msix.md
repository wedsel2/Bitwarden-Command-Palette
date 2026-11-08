# 5. Release with semantic-release: a signed MSIX with SBOM and provenance

- Status: Superseded by [20](0020-distribute-through-the-microsoft-store.md) (signing and distribution; the release pipeline stands)
- Date: 2026-11-07

## Context

Users install a Command Palette extension as an MSIX package, which Windows
only installs when it is signed by a certificate it trusts. A user should be
able to verify that a release was built by this repository's pipeline from a
given commit.

## Decision

- **semantic-release** creates the version, tag and GitHub release from
  Conventional Commits on `main`.
- The release job attaches the **MSIX built and tested in the same run**,
  never a rebuild.
- Releases are signed through **SignPath Foundation** (free code signing for
  open-source projects). Until the application is approved, builds are signed
  with a self-signed development certificate and marked as pre-releases.
- Every release carries a **CycloneDX SBOM** and a **build provenance
  attestation** (`actions/attest-build-provenance`).

## Alternatives considered

- **Azure Trusted Signing**: quick and trusted, but paid every month.
- **Self-signed only**: every user would have to trust a personal certificate
  by hand, which teaches exactly the wrong habit.
- **Manual releases**: easy to skip a step; the pipeline makes each release the
  same.

## Consequences

- SignPath requires an application and keeps its own approval step in the
  release; the project must meet its open-source criteria.
- Until SignPath is approved, installing needs developer mode or a trusted
  development certificate; the install guide must say so.
- This record is implemented in phase 5; until then a release is a tag and
  notes only.
