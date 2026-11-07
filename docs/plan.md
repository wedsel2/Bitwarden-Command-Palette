# Bitwarden Command Palette: project plan

Working draft, translated and analysed from the original Dutch design notes.
Phase 1 turns this into `docs/architecture.md`, `docs/roadmap.md`, decision
records in `docs/adr/` and `CLAUDE.md`, the same way as Garmin Analyzer.

## Goal

A PowerToys Command Palette extension that searches Bitwarden items by name and
copies a password (or username, or TOTP later) to the clipboard. Set up as a
traceable open-source project: every dependency open source and pinned, every
change verified by an automated pipeline. Security wins over convenience.

## What the analysis changed

The original notes are sound. Seven points change or sharpen them:

1. **The platform decides the language.** "Windows command palette" means the
   PowerToys Command Palette (CmdPal). Its extensions are C# (.NET, WinRT
   interfaces, packaged as MSIX). Python and Rust drop out; C# matches the notes'
   own preference for a Windows-native, verifiable binary.
2. **CLI, not the SDK.** The Bitwarden Secrets Manager SDK is under the
   Bitwarden SDK License (not OSI). The password manager SDK (`sdk-internal`) is
   GPL/dual-licensed, Rust, and explicitly "not intended for public use". The
   `bw` CLI is GPL-3.0, stable and supported. We call it as a separate process,
   so its license does not reach our MIT code.
3. **No own IPC.** CmdPal already runs each extension in its own process and
   talks to it over COM. A named pipe or localhost socket would add an attack
   surface for nothing. The UI/backend split becomes an assembly split inside
   the extension: `Core` (session, vault, clipboard; no UI references, fully
   unit-testable) and `Extension` (CmdPal pages and commands only).
   `bw serve` is rejected for the same reason: it exposes an unauthenticated
   REST API on localhost to every local process.
4. **Keep the session key in memory, not in Credential Manager.** The extension
   is a long-lived process, so the key does not need to survive anywhere else.
   Credential Manager only adds a copy that any same-user process can read, and
   that outlives a crash. If CmdPal restarts, the user unlocks again.
   (This reverses a choice in the notes; it needs your agreement.)
5. **Never put secrets on a command line.** Argument lists are readable by
   every same-user process. The master password goes to `bw unlock` through
   `--passwordenv` on the child's environment only; the session key goes in
   `BW_SESSION` on the child's environment, never in our own environment.
6. **An API key does not replace the master password.** `bw login --apikey`
   logs in, but `bw unlock` still needs the master password. Proposal: the
   extension does not log in at all. The user runs `bw login` once; the
   extension only unlocks, searches, copies and locks.
7. **The metadata cache lives in memory only.** This closes the open point on
   protecting cached names: there is no cache on disk. Names, usernames and URIs
   are loaded at unlock and dropped at lock. Honest limit: `bw list items`
   returns whole items including passwords, so they pass through our process
   briefly while we strip them. Passwords are fetched again per copy with
   `bw get password <id>`.

## Threat model (to become ADR)

In scope, the extension must protect against:

- secrets in logs, exception messages, telemetry or crash reports,
- secrets on disk in any form (no cache, no Credential Manager, no temp files),
- secrets on process command lines,
- a vault left unlocked: idle timeout, absolute cap, lock on Windows lock/sleep,
- clipboard history, cloud clipboard and clipboard managers keeping a password,
- a tampered or wrong `bw` executable,
- dependency and build supply chain.

Out of scope, stated plainly in the README: malware already running as the same
Windows user. It can read process memory, the clipboard and keystrokes, and no
design at this level stops it. .NET strings cannot be reliably zeroed either;
we minimise lifetime, not guarantee erasure.

## Security rules

Session

- Idle timeout, default 10 minutes, configurable 1 to 15. Every search or copy
  resets it.
- Absolute cap, default 60 minutes, after which the vault locks regardless.
- Lock immediately on Windows session lock, sleep and sign-out.
- Locking means: `bw lock`, drop the session key and the metadata, clear the
  clipboard if it still holds our value.
- Before every action, check the timers and `bw status`; if locked or expired,
  show the unlock page.

Master password

- Typed per session in a CmdPal form (password field). Never stored, never
  logged, passed to `bw` once through the child environment.

Clipboard

- Copy with `IsAllowedInHistory = false` and `IsRoamable = false`, plus the
  `ExcludeClipboardContentFromMonitorProcessing` format for third-party managers.
- Clear after 20 seconds (configurable), only if the clipboard still holds the
  value we put there, so a later copy by the user is not wiped.

The `bw` executable

- Path configured by the user, or found on `PATH` at a known install location.
- Before first use, verify the Authenticode signature names Bitwarden Inc.
  Refuse to run an unsigned or differently signed `bw`. (Spike confirms the
  native `bw.exe` is signed; the npm install is not, so that one is unsupported.)

Logging

- One logging wrapper; secret types (`MasterPassword`, `SessionKey`, `Secret`)
  have a `ToString()` that returns `***` and are never passed to the logger.
- `bw` stderr is never logged verbatim.

Password generation

- Out of the first release. When added, it calls `bw generate` and passes
  Bitwarden's options through; the plugin decides no lengths itself. The spike
  checks whether the CLI can read the account's generator settings.

Windows Hello

- Deferred. The only useful form would store a master-password-derived secret
  behind Hello, which is a different trust model and needs its own ADR.

## Pipeline (same spirit as Garmin Analyzer)

Runs on `windows-latest`, because MSIX and WinAppSDK need Windows.

```text
pull request / push to main
 ├─ lint          dotnet format --verify-no-changes, analyzers as errors,
 │                actionlint, Gitleaks
 ├─ test          xUnit v3 on MTP + coverlet (80% floor) → SonarQube Cloud
 │                includes the secret-leak tests (below)
 └─ scan          dotnet list package --vulnerable --include-transitive,
                  Trivy on the lock files, CodeQL (C#)
        │ all must pass
        ▼
    build         MSIX + SBOM (CycloneDX) + build provenance attestation
        │ main only
        ▼
    release       semantic-release → signed MSIX on GitHub Releases
                  → WinGet manifest (later)

mondays           Renovate (NuGet, Actions, tool versions)
```

Traceability

- Central Package Management (`Directory.Packages.props`) and NuGet lock files
  with `RestoreLockedMode` in CI; packages from nuget.org only, with
  package source mapping.
- `TreatWarningsAsErrors`, nullable enabled, .NET analyzers at `latest-all`.
  (Not SonarAnalyzer.CSharp: it is source-available since 2024, not open source.)
- Actions pinned by commit SHA; workflows default to a read-only token.
- Licence check: CI fails on a dependency that is not under an allowed OSI
  licence.
- Deterministic builds (`ContinuousIntegrationBuild`, SourceLink) so a release
  can be rebuilt from its tag.

Secret-leak tests

- A fake `bw` (small test executable) replays scripted JSON, so tests and CI
  never touch a real vault or Bitwarden server.
- Canary tests run unlock, search, copy, timeout and every error path with a
  known fake password and session key, capture all log output and exception
  messages, and fail if a canary string appears.
- A test asserts that no child process is started with a secret in its
  arguments.

Code signing (open)

- MSIX must be signed to install. Options: SignPath Foundation (free code
  signing for OSS projects, needs an application), Azure Trusted Signing (paid,
  cheap), or a self-signed certificate users must trust (poor). Proposal:
  self-signed for development, apply to SignPath for releases.

## Roadmap

| # | Phase | Outcome |
|---|---|---|
| 0 | Pipeline | Solution skeleton, CI with all gates, Renovate, semantic-release, branch protection |
| 1 | Design | Architecture doc, threat model and decision records, `CLAUDE.md`, roadmap |
| 2 | Spike | Hello-world CmdPal extension installed locally; `bw` behaviour confirmed (status/unlock/lock JSON, signature, `--passwordenv`, generator settings); findings written |
| 3 | Core | `bw` runner, session manager with timers, metadata store, clipboard service, secret types, fake `bw`, leak tests |
| 4 | Extension | Unlock form, search list, per-login actions (copy username, password or TOTP), lock command, settings page |
| 5 | Release | Signed MSIX on GitHub Releases, install guide, README with the threat model |
| 6 | Later | Open URI, WinGet listing, password generation, Windows Hello (each needs a decision first) |

## Decision records to write in phase 1

1. Host on GitHub as a public repository
2. C# / .NET toolchain: CPM, lock files, analyzers, xUnit
3. Security and quality scanning
4. Renovate for updates
5. Release: semantic-release, signed MSIX, SBOM and provenance
6. Threat model and what is out of scope
7. Bitwarden access through the `bw` CLI, not an SDK and not `bw serve`
8. No IPC of our own; Core/Extension split in one process
9. Session key in memory only, with idle timeout, absolute cap and lock events
10. Metadata in memory only; secrets fetched per action
11. Clipboard: excluded from history, cleared if unchanged
12. The extension unlocks only; login stays with the user

## Decided on 2026-11-07

- **Session key in memory only** (point 4). No Credential Manager.
- **Timeouts:** 10 minutes idle (configurable 1 to 15), 60 minutes absolute cap,
  plus lock on Windows lock and sleep.
- **Signing:** apply to SignPath Foundation for releases; self-signed until
  approved.
- **First release copies username, password or TOTP.** Selecting a login opens
  its actions: copy username, copy password, copy TOTP. Each secret is fetched
  only when its action runs (`bw get password <id>`, `bw get totp <id>`). The
  TOTP action only shows for items that have a TOTP seed; the metadata keeps a
  "has TOTP" flag, never the seed. All three copies follow the clipboard rules.

## Prior art

There is already a community "Bitwarden for Command Palette" project
(kusutori). Worth a look in the spike for how it handles sessions, both to learn
and to make clear how this one differs (traceability, threat model).
