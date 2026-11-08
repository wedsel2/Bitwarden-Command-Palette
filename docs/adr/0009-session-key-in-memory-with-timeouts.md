# 9. The session key lives in memory only, ended by idle and absolute timeouts and Windows lock

- Status: Accepted; the `bw status` check before a copy is replaced by [ADR 17](0017-copy-relies-on-bw-gets-own-session-check.md)
- Date: 2026-11-07

## Context

`bw unlock` returns a session key that stays valid until `bw lock` or
`bw logout`, however long that takes. The original notes chose to store it in
Windows Credential Manager with an expiry time. Credential Manager entries are
readable by every process of the same user and outlive the extension, including
after a crash. The extension, by contrast, is a long-lived process for as long
as Command Palette runs.

## Decision

- The session key is held **in the extension's memory only**, inside a
  `SessionKey` type ([ADR 13](0013-secret-types-and-canary-tests.md)). It is
  never written to Credential Manager, disk or our own environment.
- **Idle timeout**: 10 minutes by default, configurable from 1 to 15. A search
  or copy resets it.
- **Absolute cap**: 60 minutes by default after unlocking, configurable from 15
  to 240, regardless of activity.
- **Lock events**: Windows lock, sleep, sign-out and extension shutdown lock
  immediately.
- **Before every action** both timers and `bw status` are checked; if either
  says locked, the unlock form is shown and the action continues after it.
- **Locking** runs `bw lock`, drops the key and the vault index, and clears our
  clipboard value if it is still there.
- **At start-up** the extension runs `bw lock` first, closing any session left
  by a crash.

## Alternatives considered

- **Credential Manager with an expiry** (the original notes): survives restarts,
  but adds a copy readable by any same-user process that outlives the
  extension, and the expiry is enforced by our code only.
- **No timeout, rely on `bw lock` at exit**: leaves the vault open all day.
- **Only an idle timeout**: steady use would keep the vault unlocked forever.

## Consequences

- After a Command Palette restart, a reboot or 60 minutes, the user types the
  master password again. Accepted as the cost of security over convenience.
- Whether the lock events fire inside the extension process is a phase 2
  question; if not, `WTSRegisterSessionNotification` replaces them.
- A crash leaves `bw`'s data unlocked until the next start, but the only copy
  of the key was in the crashed process.
