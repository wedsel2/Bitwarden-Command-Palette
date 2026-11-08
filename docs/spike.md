# Phase 2 spike findings

Answers to the questions in the [roadmap](roadmap.md), from 2026-11-07. Open
items say what is needed to close them.

## Command Palette

**1. Template, framework, CI build.** Microsoft's template (in
`microsoft/PowerToys`, MIT) targets `net10.0-windows10.0.26100.0`, the framework
phase 0 already used. The extension project builds with every analyzer as an
error after two changes: `ComServer` is only async-disposable, so the template's
`UnsafeDispose` call moved into a `finally` with a justified suppression; and
CsWinRT's AOT analyzer rejects collection expressions typed as read-only
interfaces. An MSIX cannot be processor-neutral, so the project defaults to
`win-x64` when no platform is given; a plain `dotnet build` and the CI test job
keep working. Building needs the Windows SDK installed, for the CsWinRT
projection of Command Palette's interfaces.

Licences: `Microsoft.CommandPalette.Extensions` 0.12 (MIT),
`Shmuelie.WinRTServer` (MIT), `Microsoft.Windows.CsWinRT` (MIT, as a licence
file the checker cannot read), `Microsoft.Win32.SystemEvents` (MIT), and
`Microsoft.Windows.SDK.BuildTools.MSIX` (proprietary, build only). See
[ADR 14](adr/0014-windows-sdk-build-tools-as-licence-exception.md).

**2. Password field in a form.** Yes: an Adaptive Card `Input.Text` with
`"style": "password"` works in Command Palette (the prior-art extension uses
it). The value reaches the extension as JSON in the form's submit call, so it
passes through Command Palette's process, as ADR 6 already states. Whether the
host keeps a copy after the call is not documented. *Open, low priority.*

**3. Process lifetime.** Answered by the live test on 2026-11-08 (Windows 11,
Command Palette restarted from its tray icon). Command Palette starts the
extension process when it loads its extensions, before any page is opened, and
keeps it for as long as it runs. On exit it disposes the extension, and the
process then exits normally:

```
01:02:20 extension disposed by host
01:02:20 process exit
01:02:33 process 13924 started      (Command Palette started again)
```

So the extension gets a shutdown call to lock in (`SessionManager.DisposeAsync`).
Whether Command Palette also disposes an extension that sits idle for a long
time was not observed in the short run; it does not matter for locking,
because a dispose locks either way. A crash or a killed process gets no call;
that case is covered by `bw lock` at start-up (ADR 9).

**4. Session and power events.** Answered by the same live test. `SystemEvents`
works inside the extension process, which has no message loop of its own:

```
00:59:49 session switch: SessionLock        (Win+L)
01:00:05 session switch: SessionUnlock
01:01:06 session switch: SessionLock        (Start → Power → Sleep)
01:01:26 session switch: SessionUnlock
```

The test PC uses **Modern Standby** (the System log shows Kernel-Power events
506 and 507, entering and exiting Modern Standby, at 01:01:05 and 01:01:06).
Sleeping sent **no `PowerModeChanged` event at all**, only the session lock
Windows performs when it enters standby. `PowerModes.Suspend` arrives only on
older S3 sleep. Consequences:

- With the default "require sign-in after sleep", sleep locks the vault through
  `SessionLock`, so `SystemLockEvents` works as built.
- If the user sets sign-in after sleep to **Never**, Modern Standby sends
  neither event, and the vault stays unlocked through sleep until a timeout
  passes. Both timeouts run on the wall clock, so they still apply on wake.
  [ADR 16](adr/0016-lock-when-the-display-turns-off.md) closes this gap: the
  vault also locks when the display turns off.
- `SessionEnding` was not observed; sign-out also sends `SessionLogoff`.

**5. Settings storage.** The template's `SettingsManager` (toolkit
`JsonSettingsManager`) writes `settings.json` in the folder returned by the
toolkit's `Utilities.BaseSettingsPath(<extension name>)`. Settings hold no
secrets, so the exact folder does not matter for security; it is confirmed in
phase 4.

### Live test (questions 3 and 4)

Run on 2026-11-08; the results are above. The logging it relied on (`SpikeLog`) was removed afterwards; to repeat it, check out commit `babbf66` or earlier. It needs Developer Mode (Settings → System → For developers), which only you
should turn on. Then:

```powershell
dotnet build src\BitwardenCommandPalette.Extension -c Debug -p:Platform=x64
Add-AppxPackage -Register (Resolve-Path "src\BitwardenCommandPalette.Extension\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\AppxManifest.xml").Path
```

In Command Palette run **Reload**, then open **Bitwarden**. Lock Windows
(Win+L) and unlock, put the PC to sleep and wake it, leave it idle for an hour,
and open the page again in between. The newest entries show on the page. The
full log is a file; because Windows redirects a packaged app's writes to
AppData into the package's own folder, find it with:

```powershell
Get-ChildItem "$env:LOCALAPPDATA" -Recurse -Filter spike.log -ErrorAction SilentlyContinue |
  Where-Object FullName -like "*BitwardenCommandPalette*"
```
Remove the test package afterwards with
`Get-AppxPackage wedsel2.BitwardenCommandPalette | Remove-AppxPackage`.

## Bitwarden CLI (2026.10.1)

**6. Output formats.** `bw status` returns JSON with `serverUrl`, `lastSync`,
`userEmail`, `userId` and `status` (`unauthenticated`, `locked` or
`unlocked`). Recorded from a free test account on the EU server, 53 commands,
in [`tests/FakeBw/recordings/`](../tests/FakeBw/recordings/):

- `unlock --raw` prints only the session key, with no newline. `list items`
  prints one JSON array of full items, logins with their password and TOTP
  secret included, so the extension must take only what `VaultIndex` keeps
  (ADR 10). Cards and secure notes are in the same list and are told apart by
  `type` (1 login, 2 note, 3 card). `list folders` includes a "No Folder"
  entry whose `id` is an empty string.
- Every failure exits with 1, with a one-line message on stderr and nothing on
  stdout: `You are not logged in.`, `Vault is locked.`, `Not found.`, or for a
  duplicate name a list of IDs and names. **`Not found.` covers both an unknown
  ID and an item that lacks the field** (no username, no password, no TOTP, or
  a card or note), so the extension must decide from the indexed item whether a
  field exists, not from the error.
- A wrong master password gives several `ERROR bitwarden_crypto…` log lines
  from the CLI's Rust core on stderr. A missing password variable gives a
  warning and `Master password is required.` Neither ever contained the
  password (the script checked).
- `--response` wraps output as `{"success":true,"data":…}` or
  `{"success":false,"message":…}`, on stdout even for failures; the wrong
  password message there is `Cryptography error, The decryption operation
  failed`. It is easier to parse than plain stderr text.
- **Each call takes about 3 to 4 seconds** (`status` and locked calls just
  under 1 s, `sync` about 5 s): the CLI starts a Node process and decrypts the
  vault every time. Searching must work on `VaultIndex` in memory, and a copy
  costs one `bw get` call, so the extension should show that it is working.
- The master password **reprompt** flag (`reprompt: 1`) is ignored by the CLI:
  `bw get password` returns the password. Whether the extension honours it is
  a design decision for phase 4.
- A failed reprompt (`bw unlock` with a wrong password) does not end the
  current session: the phase 4 live test showed that copies from other logins
  still work afterwards without unlocking again. This settles the open
  consequence in [ADR 18](adr/0018-master-password-reprompt.md); the decision
  stands.
- Unicode names, usernames and passwords come back intact as UTF-8.
- On a fresh data folder, the first command prints `Could not find data
  file…; creating it instead.` on stderr and still succeeds, so the parser must
  go by the exit code, not by stderr text.

**7. `--passwordenv` and `BW_SESSION`.** `bw unlock --passwordenv <name>` exists
and reads the password from that environment variable. `--session` is an
argument and therefore never used; `BW_SESSION` in the child's environment is the
documented default. Unlocking invalidates every earlier session key. Useful
global flags: `--nointeraction` (never prompt, so a missing input fails instead
of hanging), `--raw`, `--response` (JSON envelope). `bw get password <id>`
also accepts a search term, so the extension always passes the item's ID.
Confirmed by the recording: unlock works through `--passwordenv`, every list
and get works with only `BW_SESSION` in the child's environment, an invalid or
pre-lock session key gives `Vault is locked.`, and `status` reports `locked`
for a key used after `bw lock`. *Open: `bw lock` was only recorded with
`BW_SESSION` set. The start-up lock runs without one; that it still ends an
earlier session needs a check in the next recording.*

**8. Signature and builds.** Both Windows builds, `bw-windows` and
`bw-oss-windows`, are Authenticode-signed by **Bitwarden Inc.** (DigiCert
code-signing CA, timestamped); both SHA-256 hashes matched the digests GitHub
publishes. The OSS build contains only GPL-3.0 code and lacks just one command,
`device-approval` (an organisation admin feature). winget installs the full
build (`GPL-3.0 or Bitwarden-1.0`). On first run `bw` creates its own
`data.json` in its data folder.

**9. TOTP edge cases.** Without Premium, `bw get totp` on an item that has a
TOTP secret (plain or `otpauth://`) fails with `Premium status is required to
use this feature.`; on an item without one it fails with `Not found.` The
secret itself is still in `list items` and `get item`. So the extension shows
"Copy TOTP" only for items with a secret and reports the Premium message as it
is. *Open, low priority: the output with Premium, which needs a Premium test
account; Bitwarden documents it as the six-digit code.*

**10. Generator settings.** *Open, phase 6.*

### Recording (questions 6, 7 and 9)

[`tools/Record-BwOutput.ps1`](../tools/Record-BwOutput.ps1) records the CLI's
answers for the fake `bw`. It needs a **new, empty Bitwarden account made only
for this**, never your real one, and runs in Windows PowerShell 5.1 or
PowerShell 7:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Record-BwOutput.ps1
```

Add `-ServerUrl https://...` for a self-hosted test server and `-BwPath` when
`bw` is not on `PATH`. The script works in a temporary bw data folder, so your
own `bw` login is untouched, and asks you to log in there with bw's own prompt,
to confirm the account's email, and for its master password (passed to `bw`
only through `--passwordenv` on the child's environment). It stops before
writing anything if the vault holds an item or folder whose name does not start
with `BWCP Fixture`. It then recreates its fixture logins (full, no TOTP, no
username, no password, TOTP as an `otpauth://` URI, master password reprompt,
duplicate names, Unicode), a card and a secure note, all with made-up values,
and records about fifty commands: status in each state, unlock with a missing
and a wrong password, list and get for every fixture, `--response` envelopes,
an invalid and a locked session.

Before writing, it replaces the account email, every ID, every date, the
session keys, your user folder and a self-hosted server URL, and refuses to
write if any of them is left. It logs out and deletes the temporary folder at
the end. Output goes to `tests/FakeBw/recordings/`, one JSON file per command
plus a `manifest.json` with the CLI version and signature. Review it with
`git diff` before it is committed.

## Prior art (question 11)

`kusutori/BitwardenForCommandPalette` (MIT, active) is a full-featured extension
with item editing, a generator and localisation. Its CLI service passes the
master password and the session key as **command-line arguments**
(`unlock "<password>"`, `--session "<key>"`), readable by every process of the
same user, and keeps the session key in a plain `string` property. Copies go
through a plain clipboard helper with no history exclusion or automatic clear,
and no idle timeout was found. This project differs exactly where its threat
model (ADR 6) requires: secrets only in the child's environment, timeouts and
lock events, a clipboard outside history that clears itself, and leak tests.

## Changes to decisions

- [ADR 7](adr/0007-bitwarden-access-through-the-cli.md) accepted: both signed
  Windows builds are supported, the OSS build is recommended.
- [ADR 14](adr/0014-windows-sdk-build-tools-as-licence-exception.md) added.
