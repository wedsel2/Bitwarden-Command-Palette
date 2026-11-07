# 1. Host on GitHub as a public repository

- Status: Accepted
- Date: 2026-11-07

## Context

The project must be traceable: anyone should be able to read the code, see how
each release was built and check what it depends on. It needs free CI with
Windows runners (MSIX packaging needs Windows), code scanning and a place to
publish releases.

## Decision

Host on GitHub Free as a public repository, with GitHub Actions for CI and
GitHub Releases for the signed MSIX packages.

## Alternatives considered

- **GitLab.com Free**: a small monthly compute quota, and Windows runners use
  it up faster; dependency scanning is a paid feature.
- **GitHub private repository**: no enforced branch protection, CodeQL or
  secret scanning on the free plan, and it defeats the traceability goal.

## Consequences

- Unlimited Actions minutes including Windows runners; CodeQL, secret scanning
  with push protection and branch rulesets at no cost.
- Everything in the repository and CI logs is world-readable: no vault data,
  test vault credentials or signing keys may ever be committed or printed.
- Third-party actions are a supply-chain risk, so they are pinned by commit SHA
  and workflows default to a read-only token.
