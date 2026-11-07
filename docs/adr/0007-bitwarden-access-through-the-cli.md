# 7. Bitwarden access through the signed bw CLI, not an SDK and not bw serve

- Status: Accepted (after the phase 2 spike, see [findings](../spike.md))
- Date: 2026-11-07

## Context

The extension needs to unlock the vault, list logins and read a password or
TOTP code. Bitwarden offers three ways in for a third-party client: the `bw`
CLI (GPL-3.0), its local REST server `bw serve`, and SDKs. The Secrets Manager
SDK has a .NET binding but covers machine secrets only and is under the
Bitwarden SDK License, which is not open source. The password manager SDK
(`sdk-internal`) is Rust, published for web, iOS and Android only, and marked as
not intended for public use.

The spike confirmed on CLI 2026.10.1 that `bw unlock --passwordenv` exists, that
`BW_SESSION` in the environment is the default way to pass the session key, and
that both Windows builds (`bw-windows` and the GPL-only `bw-oss-windows`) are
Authenticode-signed by Bitwarden Inc. winget installs the full build.

## Decision

- Use the **`bw` CLI as a separate process**, through one class (`BwCli`).
  Nothing else in the code starts a process or knows the CLI exists.
- **No secret ever goes in an argument.** The master password reaches
  `bw unlock` through `--passwordenv` with a variable set on the child's
  environment only; the session key reaches every other command as
  `BW_SESSION` on the child's environment only. Our own process environment
  never holds either. `--session` is never used.
- Every call passes `--nointeraction`, so a missing input fails instead of
  waiting for a prompt. Item lookups always pass the item's ID, never a search
  term.
- Only a **native Windows `bw.exe` signed by Bitwarden Inc** is run. Before
  first use and after the file changes, `BwCli` verifies the Authenticode
  signature and refuses an unsigned or differently signed file. Both official
  builds pass; the install guide recommends the OSS build.
- The path to `bw.exe` is a setting; without one, known install locations
  (including winget's) and `PATH` are searched.
- `bw` stderr is mapped to our own error messages and never shown or logged
  verbatim.

## Alternatives considered

- **Password manager SDK**: would allow decrypting one field at a time inside
  our process, but needs a self-maintained bridge from C# to Rust around an
  unsupported, changing interface, and GPL code linked into the extension.
- **Secrets Manager SDK**: wrong product and not open source.
- **`bw serve`**: keeps an unlocked vault behind an unauthenticated HTTP API on
  localhost, reachable by every local process.
- **Reading `data.json` and decrypting it ourselves**: an undocumented internal
  format, and our own implementation of Bitwarden's cryptography.
- **Accepting only the OSS build**: the signature is identical, so the
  extension cannot enforce it reliably, and winget users would be refused.

## Consequences

- `bw list items` returns whole items including passwords, which pass through
  our process briefly before `VaultIndex` strips them ([ADR 10](0010-metadata-in-memory-secrets-per-action.md)).
- Each action costs a process start; acceptable for copy actions, and search
  does not call `bw`.
- The npm-installed CLI (a script run by Node) is not supported, because there
  is no signed executable to verify.
- The CLI's output format becomes a dependency; the fake `bw` in the tests is
  built from real output recorded from a test vault, and a CLI update that
  changes it fails tests rather than users.
- Revisit if Bitwarden publishes a supported .NET binding of the password
  manager SDK under an open-source licence.
