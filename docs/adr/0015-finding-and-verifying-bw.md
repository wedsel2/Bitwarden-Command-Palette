# 15. How bw.exe is found and how its signature is checked

- Status: Proposed (confirm the revocation and search rules below)
- Date: 2026-11-07

## Context

[ADR 7](0007-bitwarden-access-through-the-cli.md) decides that only a native
`bw.exe` signed by Bitwarden Inc. is run, and that known install locations and
`PATH` are searched when no path is set. It leaves open which locations, what
"signed by Bitwarden Inc." is checked against, what happens when the
certificate's revocation status cannot be fetched, and how often the check runs.
Package managers put unsigned shims on `PATH` (Chocolatey, Scoop) or a symbolic
link (winget's `Links` folder). The recordings showed both official builds
signed with `CN=Bitwarden Inc., O=Bitwarden Inc.`.

## Decision

- **Order.** A path set in the settings is used alone: if it is missing or not
  signed, the extension says so and does not fall back. Otherwise, in order:
  winget's `Links\bw.exe`, winget's `Packages\Bitwarden.CLI_*\bw.exe`, Scoop's
  `apps\bitwarden-cli\current\bw.exe`, Chocolatey's
  `lib\bitwarden-cli\tools\bw.exe`, then each fully qualified `PATH` entry. Only
  `bw.exe` is considered; the npm `bw.cmd` is not.
- **Links** are followed to their final target, and the target is what is
  checked and what `BwCli` starts. The verifier refuses a link passed to it
  directly.
- **Relative paths** are never used: not a relative `PATH` entry, not a relative
  configured path, and `BwCli` refuses a path that is not fully qualified.
- **The check**: `WinVerifyTrust` must validate the file's embedded Authenticode
  signature and chain, and the signing certificate's common name and
  organisation must both be exactly `Bitwarden Inc.`. The first file that passes
  wins; a shim or other unsigned file is skipped.
- **Revocation** is checked for the whole chain except the root. If the
  revocation server cannot be reached, the signature is still accepted; a
  revoked, expired-without-timestamp, tampered or untrusted signature is not.
- **When**: `BwCli` checks before every start, off the caller's thread and
  within the call's timeout and cancellation, since a revocation fetch can be
  slow. A passed check is remembered per file while its size and last write time
  stay the same, shared between the locator and `BwCli`; a failed check is never
  remembered.
- `wintrust.dll` is loaded from System32 only.

## Alternatives considered

- **Fail when revocation cannot be checked**: the vault can be unlocked offline,
  and refusing then would break that for a risk (a revoked Bitwarden
  certificate together with a blocked network) that is far smaller than a
  same-user attacker, who is out of scope anyway.
- **Pin the certificate's thumbprint or public key**: Bitwarden renews its
  certificate, and each renewal would lock every user out until a release.
- **Check only the common name**: the organisation is checked too, at no cost.
- **Falling back from a configured path** to the search: the user chose that
  file; silently running a different one hides a problem.
- **Checking once at start-up only**: an updated or replaced file would run
  unchecked until the next restart.

## Consequences

- Chocolatey and Scoop users work without configuring a path, because their
  shims are skipped in favour of the real file in the package folder.
- A same-user process could swap the file between the check and the start, or
  replace it with one of the same size and restore the write time so the
  remembered result is reused; same-user malware is out of scope (ADR 6).
- Accepting an unreachable revocation server is weaker than a hard failure for
  threat 6 in ADR 6 ("a wrong or tampered `bw.exe`"), only in the case above.
- The search list needs updating if a package manager changes its layout.
