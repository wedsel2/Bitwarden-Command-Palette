# Architecture Decision Records

One file per decision, numbered in order. A record is not edited after acceptance
except to change its status; a changed decision gets a new record that supersedes
the old one.

| # | Decision | Status |
|---|---|---|
| [1](0001-host-on-github-public.md) | Host on GitHub as a public repository | Accepted |
| [2](0002-dotnet-toolchain.md) | .NET toolchain: pinned SDK, central packages, lock files, analyzers, xUnit v3 | Accepted; SDK roll-forward superseded by [21](0021-pin-the-dotnet-sdk-exactly.md) |
| [3](0003-security-and-quality-scanning.md) | Security and quality scanning, open-source dependencies only | Accepted |
| [4](0004-dependency-updates-with-renovate.md) | Renovate for dependency updates | Accepted |
| [5](0005-releases-signed-msix.md) | Release with semantic-release: a signed MSIX with SBOM and provenance | Superseded by [20](0020-distribute-through-the-microsoft-store.md) |
| [6](0006-threat-model.md) | Threat model: what the extension protects against, and what it does not | Accepted |
| [7](0007-bitwarden-access-through-the-cli.md) | Bitwarden access through the signed bw CLI, not an SDK and not bw serve | Accepted |
| [8](0008-no-ipc-core-and-extension-in-one-process.md) | No IPC of our own: Core and Extension as two assemblies in one process | Accepted |
| [9](0009-session-key-in-memory-with-timeouts.md) | The session key lives in memory only, ended by idle and absolute timeouts and Windows lock | Accepted |
| [10](0010-metadata-in-memory-secrets-per-action.md) | Login metadata in memory only; passwords and TOTP fetched per action | Accepted |
| [11](0011-clipboard-outside-history-cleared-if-unchanged.md) | Copied secrets stay out of clipboard history and are cleared if unchanged | Accepted |
| [12](0012-the-extension-unlocks-it-never-logs-in.md) | The extension unlocks; logging in stays with the user | Accepted |
| [13](0013-secret-types-and-canary-tests.md) | Secrets are types that cannot be printed, and canary tests prove nothing leaks | Accepted |
| [14](0014-windows-sdk-build-tools-as-licence-exception.md) | Microsoft's Windows SDK build tools are the one exception to the open-source rule | Accepted |
| [15](0015-finding-and-verifying-bw.md) | How bw.exe is found and how its signature is checked | Accepted |
| [16](0016-lock-when-the-display-turns-off.md) | The vault locks when the display turns off | Accepted |
| [17](0017-copy-relies-on-bw-gets-own-session-check.md) | A copy relies on bw get's own session check | Accepted |
| [18](0018-master-password-reprompt.md) | Logins marked for reprompt ask for the master password before each secret copy | Accepted |
| [19](0019-website-icons-from-the-bitwarden-icon-server.md) | Website icons come from the vault's Bitwarden icon server and stay in memory | Accepted |
| [20](0020-distribute-through-the-microsoft-store.md) | Distribute through the Microsoft Store; GitHub releases stay as the verifiable copy | Accepted |
| [21](0021-pin-the-dotnet-sdk-exactly.md) | Pin the .NET SDK to an exact version; patches arrive as reviewed bumps | Accepted |

New records use the same sections: Context, Decision, Alternatives considered,
Consequences.
