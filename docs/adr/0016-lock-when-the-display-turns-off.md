# 16. The vault locks when the display turns off

- Status: Accepted
- Date: 2026-11-08

## Context

[ADR 9](0009-session-key-in-memory-with-timeouts.md) locks the vault on
Windows lock, sleep and sign-out. The phase 2 live test
([spike findings](../spike.md), question 4) showed that on a PC with Modern
Standby, the usual sleep on current laptops, Windows sends no
`PowerModeChanged` event at all. Sleep only locks the vault because Windows
locks the session when it enters standby. With "require sign-in after sleep"
set to Never, standby sends neither event, and the vault stays unlocked through
sleep until a timeout passes.

Modern Standby always turns the display off. Windows reports the console
display state (on, off, dimmed) through `GUID_CONSOLE_DISPLAY_STATE`, which a
process can subscribe to with `PowerSettingRegisterNotification` and a
callback, without a window of its own.

## Decision

- `SystemLockEvents` also subscribes to `GUID_CONSOLE_DISPLAY_STATE` with
  `PowerSettingRegisterNotification` and a callback, and unsubscribes with
  `PowerSettingUnregisterNotification` when disposed.
- A change to **off** requests a lock with `LockReason.DisplayOff`. **Dimmed**
  and **on** do not lock.
- This is in addition to the events of ADR 9, which stay as they are.

## Alternatives considered

- **Accept the gap and document it**: leaves a setting a user may well choose
  (no sign-in after sleep) silently weakening ADR 9, for a protection that
  costs one callback.
- **`PowerRegisterSuspendResumeNotification`**: reports suspend and resume,
  which is the same information `PowerModeChanged` failed to deliver on Modern
  Standby.
- **`GUID_MONITOR_POWER_ON`**: deprecated in favour of the console display
  state, and it reports one monitor rather than the session's display.
- **A window of our own for `WM_POWERBROADCAST`**: works, but needs a message
  loop the extension does not have; the callback does not.

## Consequences

- Sleep locks the vault whatever the sign-in setting is.
- The vault also locks when the screen turns off because of the display
  timeout while the user is away, and when the user turns the display off.
  Accepted: it matches "security over convenience", and the display timeout
  is usually longer than active use.
- The callback runs on a Windows thread pool thread, like the other events; the
  lock it requests is the same one ADR 9 describes.
- Strengthens the threat model ([ADR 6](0006-threat-model.md), "a vault left
  unlocked"); weakens nothing.
