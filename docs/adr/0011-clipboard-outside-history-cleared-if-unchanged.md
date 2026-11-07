# 11. Copied secrets stay out of clipboard history and are cleared if unchanged

- Status: Accepted
- Date: 2026-11-07

## Context

Copying is the extension's main output. Windows clipboard history (Win+V) and
cloud clipboard keep copies by default, clipboard managers can store them, and
a password left on the clipboard is pasted into the wrong place sooner or later.

## Decision

- Every copy of a username, password or TOTP code goes through
  `ClipboardService`, which sets the content with
  `ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false }`
  and adds the `ExcludeClipboardContentFromMonitorProcessing` format for
  third-party clipboard managers.
- After a delay (20 seconds by default, configurable from 5 to 120) the
  clipboard is cleared, **only if it still holds the value we put there**. A
  later copy by the user is never wiped.
- Locking the vault clears it under the same condition.
- Usernames are treated the same way as passwords: one rule for every copy.

## Alternatives considered

- **Clearing unconditionally**: would wipe whatever the user copied after us.
- **No automatic clear**: leaves the password until the next copy.
- **Typing the password into the target window** instead of copying: needs
  focus tracking and synthetic keystrokes, and fails in elevated windows.

## Consequences

- Checking "still ours" means comparing the current clipboard text with the
  value we copied, so that value is held in memory for the delay; it is held as
  a `Secret` and dropped after the check.
- Applications that ignore the exclusion formats can still record the copy;
  that is outside the extension's control and stated in the README.
