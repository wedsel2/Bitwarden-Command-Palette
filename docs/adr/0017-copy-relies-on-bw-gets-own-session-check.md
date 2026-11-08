# 17. A copy relies on bw get's own session check

- Status: Accepted
- Date: 2026-11-08

## Context

[ADR 9](0009-session-key-in-memory-with-timeouts.md) says that before every
action both timers and `bw status` are checked. In the live test of phase 4 a
password copy took about 4 seconds: about 1 second for `bw status` and about 3
for `bw get`, because the CLI starts Node and decrypts the vault on every call
([spike findings](../spike.md), question 6).

The spike recording showed that `bw get` with an invalid or pre-lock session
key fails with `Vault is locked.`, which `BwCli` already reports as
`BwErrorKind.VaultLocked`. `bw status` adds no protection to a password or TOTP
copy that `bw get` does not already give.

## Decision

- A password or TOTP copy checks both timers, then runs `bw get` directly. If
  `bw get` reports the vault locked or the CLI logged out, the session locks
  with `LockReason.CliLocked`, as before.
- A username copy checks both timers and copies from the index without any
  `bw` call. The username is metadata already held in memory (ADR 10).
- Unlocking still starts with `bw status`, to tell "not logged in" apart
  (ADR 12).
- Everything else in ADR 9 stays as it is.

## Alternatives considered

- **Keep the status check**: about a second more on every copy, for no
  protection `bw get` does not already give.
- **`bw serve`**: one long-running CLI that answers over a local HTTP API would
  make copies near instant, but any process of the same user could call it.
  That widens the attack surface ADR 6 limits; it needs its own record if ever
  considered.

## Consequences

- A password or TOTP copy takes about 3 seconds instead of 4; a username copy
  is immediate.
- If the CLI was locked from outside (for example `bw lock` in a terminal), a
  username copy still succeeds, because it never asks bw. The next password or
  TOTP copy finds the vault locked and locks the session.
- Weakens nothing in [ADR 6](0006-threat-model.md): the timeouts and lock
  events of ADR 9 still apply before every action, and no secret is copied
  without a valid session.
