# Architecture Decision Records

One file per decision, numbered in order. A record is not edited after acceptance
except to change its status; a changed decision gets a new record that supersedes
the old one.

| # | Decision | Status |
|---|---|---|
| [1](0001-host-on-github-public.md) | Host on GitHub as a public repository | Accepted |
| [2](0002-dotnet-toolchain.md) | .NET toolchain: pinned SDK, central packages, lock files, analyzers, xUnit v3 | Accepted |
| [3](0003-security-and-quality-scanning.md) | Security and quality scanning, open-source dependencies only | Accepted |
| [4](0004-dependency-updates-with-renovate.md) | Renovate for dependency updates | Accepted |
| [5](0005-releases-signed-msix.md) | Release with semantic-release: a signed MSIX with SBOM and provenance | Accepted |
| [6](0006-threat-model.md) | Threat model: what the extension protects against, and what it does not | Accepted |
| [7](0007-bitwarden-access-through-the-cli.md) | Bitwarden access through the signed bw CLI, not an SDK and not bw serve | Proposed |
| [8](0008-no-ipc-core-and-extension-in-one-process.md) | No IPC of our own: Core and Extension as two assemblies in one process | Accepted |
| [9](0009-session-key-in-memory-with-timeouts.md) | The session key lives in memory only, ended by idle and absolute timeouts and Windows lock | Accepted |
| [10](0010-metadata-in-memory-secrets-per-action.md) | Login metadata in memory only; passwords and TOTP fetched per action | Accepted |
| [11](0011-clipboard-outside-history-cleared-if-unchanged.md) | Copied secrets stay out of clipboard history and are cleared if unchanged | Accepted |
| [12](0012-the-extension-unlocks-it-never-logs-in.md) | The extension unlocks; logging in stays with the user | Accepted |
| [13](0013-secret-types-and-canary-tests.md) | Secrets are types that cannot be printed, and canary tests prove nothing leaks | Accepted |

New records use the same sections: Context, Decision, Alternatives considered,
Consequences.
