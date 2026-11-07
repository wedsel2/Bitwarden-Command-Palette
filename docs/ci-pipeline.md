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
    release       semantic-release → tag and GitHub release

mondays           Renovate update pull requests (hosted app, not a workflow)
```

The signed MSIX, its SBOM and build provenance join the release job once the
extension project exists (roadmap phase 5).

| File | Purpose |
|---|---|
| `.github/workflows/ci.yml` | Lint, test, scan, release |
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
needs tests lives in Core (ADR 8).

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

Steps 3 to 5 can be applied with `gh api`; steps 1 and 2 need the browser.

## Local use

```bash
dotnet tool restore
uvx pre-commit install
dotnet build
dotnet test --coverlet --coverlet-threshold 80
uvx pre-commit run --all-files
```
