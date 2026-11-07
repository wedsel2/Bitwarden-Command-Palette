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

**3. Process lifetime.** *Open: needs the live test below.* The spike build
logs process start, page opens, host dispose and process exit.

**4. Session and power events.** *Open: needs the live test below.* The spike
build subscribes to `SystemEvents.SessionSwitch`, `PowerModeChanged` and
`SessionEnding` and logs what arrives.

**5. Settings storage.** The template's `SettingsManager` (toolkit
`JsonSettingsManager`) writes `settings.json` in the folder returned by the
toolkit's `Utilities.BaseSettingsPath(<extension name>)`. Settings hold no
secrets, so the exact folder does not matter for security; it is confirmed in
phase 4.

### Live test (questions 3 and 4)

Needs Developer Mode (Settings → System → For developers), which only you
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
`unlocked`), per its help text. *Open: the outputs of `unlock --raw`,
`list items` and `get` need a recording from a throwaway test vault, which you
run; see [Recording](#recording-questions-6-7-and-9).*

**7. `--passwordenv` and `BW_SESSION`.** `bw unlock --passwordenv <name>` exists
and reads the password from that environment variable. `--session` is an
argument and therefore never used; `BW_SESSION` in the child's environment is the
documented default. Unlocking invalidates every earlier session key. Useful
global flags: `--nointeraction` (never prompt, so a missing input fails instead
of hanging), `--raw`, `--response` (JSON envelope). `bw get password <id>`
also accepts a search term, so the extension always passes the item's ID.

**8. Signature and builds.** Both Windows builds, `bw-windows` and
`bw-oss-windows`, are Authenticode-signed by **Bitwarden Inc.** (DigiCert
code-signing CA, timestamped); both SHA-256 hashes matched the digests GitHub
publishes. The OSS build contains only GPL-3.0 code and lacks just one command,
`device-approval` (an organisation admin feature). winget installs the full
build (`GPL-3.0 or Bitwarden-1.0`). On first run `bw` creates its own
`data.json` in its data folder.

**9. TOTP edge cases.** *Open: needs the test vault recording.*

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
