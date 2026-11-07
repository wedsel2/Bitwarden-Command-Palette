# 6. Threat model: what the extension protects against, and what it does not

- Status: Accepted
- Date: 2026-11-07

## Context

Every security decision in this project is a trade-off against convenience.
Without an explicit threat model, each one gets argued from scratch, and the
README would promise more than the design delivers.

## Decision

The extension must protect against:

1. **Secrets in logs, exception messages or crash output** of our own code.
2. **Secrets on disk** in any form: no cache, no Credential Manager entry, no
   temporary file, no settings value.
3. **Secrets on a process command line**, where every same-user process can
   read them.
4. **A vault left unlocked**: idle timeout, absolute cap, lock on Windows lock,
   sleep and sign-out.
5. **Secrets kept by the clipboard**: history, cloud clipboard, clipboard
   managers, or simply left there.
6. **A wrong or tampered `bw.exe`**.
7. **A compromised dependency or build**: pinned packages, licence and
   vulnerability checks, signed releases with provenance.

Out of scope, stated plainly in the README:

- **Malware running as the same Windows user.** It can read the extension's
  memory, the clipboard and keystrokes; no design at this level stops it.
- **Erasing secrets from memory.** .NET strings are immutable and may be copied
  by the runtime. The design keeps secrets short-lived; it does not claim to
  wipe them.
- **A compromised Bitwarden account or server**, and the security of `bw`
  itself.
- **Command Palette itself**: the master password passes through its process
  on the way from the form to the extension.

## Alternatives considered

- **Including same-user malware**: would need a separate process under another
  account or hardware-backed isolation, far beyond a palette extension, and
  Bitwarden's own clients do not claim it either.
- **No written threat model**: decisions would drift, and users could not judge
  the extension.

## Consequences

- Each security rule in the code and in `CLAUDE.md` traces back to one of the
  seven items above.
- Some protections (for example, keeping `BW_SESSION` out of our own
  environment) mainly raise the bar against out-of-scope attackers; they stay
  because they cost little.
- A change that weakens one of the seven needs a new record.
