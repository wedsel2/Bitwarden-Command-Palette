# 18. Logins marked for reprompt ask for the master password before each secret copy

- Status: Accepted
- Date: 2026-11-08

## Context

Bitwarden lets a user mark a login "Master password re-prompt". Bitwarden's own
apps then ask for the master password again before they show, copy or autofill
its password or TOTP code. The CLI ignores the flag: `bw get password` returns
the password of such a login like any other ([spike findings](../spike.md),
question 6). `VaultIndex` keeps the flag as `BwLogin.RequiresReprompt`.

The CLI has no command that only checks a master password. The one check it
offers is `bw unlock`, which also issues a new session key and makes every
earlier key worthless.

## Decision

- A **password or TOTP** copy of a login marked for reprompt needs the master
  password, typed for that copy. Without it, `SessionManager.CopyAsync` returns
  `CopyOutcome.RepromptRequired` and fetches nothing.
- With it, `CopyAsync` runs `bw unlock` with that password. A wrong password
  returns `CopyOutcome.WrongPassword` and copies nothing. A right one replaces
  the session key with the new one, then the value is fetched and copied with
  it.
- The password is asked for **every** such copy; nothing is remembered between
  copies.
- A reprompt counts as activity for the idle timeout, but never extends the
  absolute timeout: that still runs from the first unlock.
- A **username** copy of such a login needs no reprompt, as in Bitwarden's own
  apps.
- The extension asks with the same master password form as the unlock page.

## Alternatives considered

- **Ignore the flag**: silently drops a protection the user chose for their
  most sensitive logins.
- **Refuse those copies**: safe, but makes the extension useless for exactly
  the logins the user cares most about.
- **Check the password ourselves** by deriving the master key: means
  reimplementing Bitwarden's key derivation (PBKDF2 or Argon2id with the
  account's settings) and holding more key material in our process.
- **Remember a reprompt for a few minutes**: convenient, but weaker than
  Bitwarden's own apps, which ask each time.

## Consequences

- A reprompt copy takes about 4 seconds longer (`bw unlock` before `bw get`).
- A copy of another login that is waiting for `bw` while a reprompt replaces
  the key fails with "vault locked" and locks the session; the user unlocks
  again. Accepted as rare.
- Whether a failed `bw unlock` (wrong reprompt password) also ends the current
  session is not yet confirmed against the real CLI. If it does, the next
  action finds the vault locked and locks the session, so it fails safe; the
  phase 4 live test checks it.
- Strengthens the threat model ([ADR 6](0006-threat-model.md), "a vault left
  unlocked"); weakens nothing.
