# CI/CD pipeline

How the pipeline works and what has to be configured outside the repository.

## Flow

```text
pull request / push to main
 ├─ lint          pre-commit: file hygiene, actionlint, Gitleaks
 ├─ test          Windows: locked restore + NuGet audit → dotnet format →
 │                build (analyzers as errors) → tests + coverage → SonarQube Cloud
 │                → dependency licence check
 └─ scan-source   Trivy on the NuGet lock files
        │ all three must pass, main only
        ▼
    version       semantic-release --dry-run → the next version, or none
        │ only when the commits warrant a release
        ▼
    package       Windows: signed MSIX for x64 and ARM64, CycloneDX SBOM,
        │         SHA-256 sums; build provenance and SBOM attestations
        ▼
    release       semantic-release → tag and draft release with the packages,
                  published as a pre-release while signed with the development certificate

mondays           Renovate update pull requests (hosted app, not a workflow)
```

The packages are built from the same commit and in the same workflow run as
the checks that passed, never rebuilt afterwards (ADR 5). The release job
checks that semantic-release still picks the version the packages were built
for.

| File | Purpose |
|---|---|
| `.github/workflows/ci.yml` | Lint, test, scan, version, package, release |
| `.github/scripts/New-ReleasePackages.ps1` | Builds and signs the MSIX packages, the SBOM and the sums; runs locally too |
| `tools/New-DevSigningCertificate.ps1` | Creates the development signing certificate and sets its secrets |
| `.pre-commit-config.yaml` | Hooks shared by local commits and the lint job |
| `global.json` | Pins the .NET SDK and selects Microsoft Testing Platform |
| `Directory.Build.props` | Analyzer, lock file, audit and determinism settings for every project |
| `Directory.Packages.props` | The only place package versions are set |
| `nuget.config` | nuget.org as the only package source |
| `dotnet-tools.json` | Pinned versions of the SonarQube scanner and the licence checker |
| `allowed-licenses.json` | Licences a dependency may have |
| `license-overrides.json` | Open-source licences the checker cannot read, per exact version |
| `license-exceptions.json` | Packages exempt from the licence rule (only Windows SDK build tools, ADR 14) |
| `renovate.json` | Dependency update policy |
| `.releaserc.json` | semantic-release configuration |

## Gates

A change cannot be merged unless:

- all pre-commit hooks pass, including Gitleaks,
- every NuGet lock file is current (`RestoreLockedMode` in CI),
- no dependency, direct or transitive, has a known high or critical
  vulnerability (NuGet audit at restore, and Trivy on the lock files),
- `dotnet format` reports no changes,
- the build has no warnings, with all .NET analyzers enabled (`latest-all`),
- tests pass with at least 80% line coverage,
- the SonarQube Cloud quality gate passes,
- every dependency, including build and test packages, has a licence listed in
  `allowed-licenses.json`.

### Open source only

Every package the build restores is under an OSI licence, and CI enforces it,
with one named exception: Microsoft's Windows SDK build tools, which an MSIX
package cannot be built without and which ship nothing in the extension
([ADR 14](adr/0014-windows-sdk-build-tools-as-licence-exception.md)).
SonarAnalyzer.CSharp is deliberately not referenced: since November 2024 it is
under the Sonar Source-Available License, which is not open source.

The Command Palette layer (`src/BitwardenCommandPalette.Extension`) is excluded
from SonarQube coverage: it holds pages and commands only, and all logic that
needs tests lives in Core (ADR 8). So is `tools/`: scripts the maintainer runs
by hand, such as the `bw` recording script, which is not part of the product.

SonarQube Cloud is an external service, like GitHub itself. Its scanner is
LGPL-3.0, but during the CI run it downloads Sonar's own analyzers, which are
source-available. Nothing from it ends up in the build output or the product.

## Releasing

Releases are driven by commit messages on `main`:

| Commit type | Result |
|---|---|
| `fix: ...` | Patch release |
| `feat: ...` | Minor release |
| `feat!: ...` or a `BREAKING CHANGE:` footer | Major release |
| `docs:`, `chore:`, `ci:`, `test:`, `refactor:` | No release |

A release attaches:

| File | What it is |
|---|---|
| `BitwardenCommandPalette-<version>-x64.msix`, `-arm64.msix` | The signed, self-contained, trimmed extension packages |
| `BitwardenCommandPalette-<version>.cdx.json` | CycloneDX SBOM of the extension and Core; build-only tools have scope `excluded` |
| `BitwardenCommandPalette-signing.cer` | The public half of the signing certificate |
| `SHA256SUMS.txt` | SHA-256 of each file above |

Each MSIX also has a build provenance attestation and an SBOM attestation,
checked with `gh attestation verify` ([install guide](install.md)).

### Signing

Until SignPath Foundation signs the releases (ADR 5), they are signed with a
self-signed development certificate whose subject, `CN=wedsel2`, matches the
`Publisher` in `Package.appxmanifest`. Its key exists only in the Actions
secrets `DEV_SIGNING_CERTIFICATE` (the PFX, base64) and
`DEV_SIGNING_CERTIFICATE_PASSWORD`. The package job imports it into the
runner's certificate store, the MSIX tooling signs from the store by
thumbprint with an RFC 3161 timestamp, and the certificate is removed again.
Without the secrets, the package job fails and nothing is released.

To create or replace the certificate, run once:

```bash
powershell -File tools/New-DevSigningCertificate.ps1
```

It prints the new thumbprint. Put it in [docs/install.md](install.md), step 4,
in the same pull request that announces the change: users compare it before
trusting the certificate.

### Trying a release build locally

With a code signing certificate for `CN=wedsel2` in `Cert:\CurrentUser\My`
(a throwaway one from `New-SelfSignedCertificate -Type CodeSigningCert` is
enough to test the build):

```bash
powershell -File .github/scripts/New-ReleasePackages.ps1 -Version 0.1.0 -CertificateThumbprint <thumbprint> -OutDir dist
```

## One-time setup outside the repository

These settings are not stored in the repository and must be redone if it is recreated.

1. **SonarQube Cloud**: import the repository, switch the analysis method from
   Automatic Analysis to CI-based, and add the token as the `SONAR_TOKEN`
   Actions secret. Check that the organisation (`wedsel2`) and project key
   (`wedsel2_Bitwarden-Command-Palette`) match the workflow.
2. **Renovate**: install the Mend Renovate GitHub App on the repository.
3. **Security settings** (Settings → Advanced Security): enable Dependabot
   alerts, CodeQL default setup (C# and Actions), secret scanning and push
   protection. Leave Dependabot version and security updates off.
4. **Branch ruleset `main`** on the default branch, without bypass: require a
   pull request merged by squash, the status checks `lint`, `test` and
   `scan-source` from GitHub Actions, and linear history; block force pushes
   and deletion.
5. **Merging**: squash merging only, with the pull request title as the commit
   title and its description as the body, so the Conventional Commit type
   decides the release. Allow auto-merge (for Renovate) and delete branches
   after merging.

6. **Signing certificate**: run `tools/New-DevSigningCertificate.ps1` (see
   [Signing](#signing)). Later, apply to SignPath Foundation and replace this
   step with its setup (ADR 5).

Steps 3 to 6 can be applied with `gh api` or `gh secret`; steps 1 and 2 need
the browser.

## Local use

```bash
dotnet tool restore
uvx pre-commit install
dotnet build
dotnet test --coverlet --coverlet-threshold 80
uvx pre-commit run --all-files
```
