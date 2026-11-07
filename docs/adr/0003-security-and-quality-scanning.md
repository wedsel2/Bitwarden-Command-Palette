# 3. Security and quality scanning, open-source dependencies only

- Status: Accepted
- Date: 2026-11-07

## Context

The extension handles passwords, so known-vulnerable dependencies, leaked
secrets and unreviewed licences are not acceptable. The original notes require
open-source libraries only. Everything should run on free tiers.

## Decision

- **NuGet audit** at restore fails on a known high or critical vulnerability,
  direct or transitive.
- **Trivy** scans the NuGet lock files on every run, as a second source.
- **nuget-license** fails CI when any package, including build and test
  packages, has a licence outside `allowed-licenses.json` (OSI licences only).
- **SonarQube Cloud** (free plan, CI-based) for quality tracking, coverage and
  the quality gate.
- **GitHub CodeQL default setup**, **secret scanning** and **push protection**
  in repository settings. **Gitleaks** runs in pre-commit.
- **actionlint** checks the workflows.

## Alternatives considered

- **SonarAnalyzer.CSharp in the build**: since November 2024 it is under the
  Sonar Source-Available License, which is not open source.
- **Dropping SonarQube Cloud entirely**: possible; kept for now because it is
  an external service like GitHub, its scanner is LGPL-3.0, and nothing from it
  reaches the build output. Revisit if the open-source rule should cover
  services too.
- **Snyk or Grype**: need an account or add little over NuGet audit plus Trivy.

## Consequences

- A newly published vulnerability can fail an unrelated pull request; the fix
  is a dependency update, which Renovate usually proposes first.
- A package without a machine-readable licence fails the check and needs an
  explicit decision, not a quiet exception.
- The SonarQube step is skipped without `SONAR_TOKEN`, so it is never the only gate.
- CodeQL and secret scanning settings live outside the repository; they are
  listed in [the pipeline documentation](../ci-pipeline.md).
