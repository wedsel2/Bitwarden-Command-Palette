# Architecture

Target design. What exists today is the pipeline, the secret types, `BwCli`,
`BwLocator` with the signature check and `VaultIndex` in `Core`, the fake `bw` for tests, and a placeholder extension that loads in
Command Palette.
See the [roadmap](roadmap.md) for the order of work and the
[decision records](adr/README.md) for the reasoning.

## Goal

A PowerToys Command Palette extension that finds Bitwarden logins by name and
copies a username, password or TOTP code. Every dependency is open source and
pinned, every change passes the pipeline, and when security and convenience
conflict, security wins.

## Components

```text
 PowerToys Command Palette (host process)
        │  WinRT / COM, out of process
        ▼
 ┌─ Extension process ─────────────────────────────────────────────┐
 │                                                                  │
 │  BitwardenCommandPalette.Extension    pages, commands, settings  │
 │        │  calls only the Core interfaces                        │
 │        ▼                                                         │
 │  BitwardenCommandPalette.Core                                    │
 │    SessionManager   state, idle and absolute timers, lock events │
 │    VaultIndex       names, usernames, URIs, "has TOTP" flags     │
 │    BwCli            the only code that starts bw.exe             │
 │    ClipboardService copy without history, clear if unchanged     │
 │    Secret types     MasterPassword, SessionKey, Secret           │
 │                                                                  │
 └────────│─────────────────────────────────────────────────────────┘
          │  child process, secrets in its environment only
          ▼
   bw.exe (Bitwarden CLI, signed by Bitwarden Inc, installed by the user)
          │
          ├─► %APPDATA%\Bitwarden CLI\data.json   encrypted vault, managed by bw
          └─► Bitwarden server                    only for bw sync
```

| Component | Responsibility |
|---|---|
| **Extension** | Command Palette pages and commands: unlock form, search list, per-login actions, lock command, settings page. No Bitwarden or Windows security logic. |
| **SessionManager** | Holds the session key in memory, runs the timers, listens for Windows lock and sleep, decides when to lock ([ADR 9](adr/0009-session-key-in-memory-with-timeouts.md)). |
| **VaultIndex** | The searchable list of logins, without secrets, loaded at unlock and dropped at lock ([ADR 10](adr/0010-metadata-in-memory-secrets-per-action.md)). |
| **BwCli** | Starts `bw.exe` only after checking its signature, with arguments that never contain a secret, and parses its JSON ([ADR 7](adr/0007-bitwarden-access-through-the-cli.md)). `BwLocator` finds a signed `bw.exe` in the settings, the known install locations or `PATH` ([ADR 15](adr/0015-finding-and-verifying-bw.md)). |
| **ClipboardService** | Puts a secret on the clipboard outside history and cloud sync, and clears it later if it is still ours ([ADR 11](adr/0011-clipboard-outside-history-cleared-if-unchanged.md)). Only its `Win32Clipboard` touches the Windows clipboard. |
| **Secret types** | Wrap every secret so it cannot be printed or logged by accident ([ADR 13](adr/0013-secret-types-and-canary-tests.md)). |

The extension and Core run in one process. Command Palette already isolates
the extension in its own process, so there is no pipe or socket of our own
([ADR 8](adr/0008-no-ipc-core-and-extension-in-one-process.md)).

## Flows

### Unlock

1. The user opens the extension. `SessionManager` is `Locked`, so the extension
   shows the unlock form with a password field.
2. `BwCli` runs `bw status`. If the CLI is not logged in, the extension says so
   and tells the user to run `bw login` once in a terminal
   ([ADR 12](adr/0012-the-extension-unlocks-it-never-logs-in.md)).
3. `BwCli` runs `bw unlock --passwordenv BWCP_PW --raw` with the master password
   in that variable of the child's environment only. The session key comes back
   on stdout.
4. `SessionManager` keeps the key in memory and starts both timers. The master
   password is dropped.
5. `BwCli` runs `bw sync` (if the last sync is older than the configured
   interval) and `bw list items`. `VaultIndex` keeps the login metadata and
   discards every secret field from the parsed output.

### Search

Search filters `VaultIndex` in memory. No `bw` call, no secret involved. It
counts as activity and resets the idle timer.

Every word of the query must match the name, a website or the username,
ignoring case, accents and character width; a word of only invisible characters
is ignored. Better matches come first: any match in the name over any match in a
website over any match in the username, then an exact match over the start of
the field over the start of a word over the middle of one, and a name that
matches the whole phrase first of all. A website is only its host without
`www.`, also for a URI stored without a scheme, so a path or query (which can
hold a token) is never searched. Ties are ordered by name. An empty query lists every login by name, and at
most 100 results are returned by default. A load replaces the whole list at
once, so a search on another thread never sees a half-loaded list.

### Copy

1. The user picks a login and an action: copy username, password or TOTP.
2. `SessionManager` checks both timers and `bw status`. If either says locked,
   the unlock form comes back and the action runs after unlocking.
3. Username comes from `VaultIndex`. Password and TOTP are fetched now with
   `bw get password <id>` or `bw get totp <id>`, never earlier.
4. `ClipboardService` copies it and schedules the clear.

### Lock

Triggered by the idle timer (default 10 minutes), the absolute timer (default
60 minutes), Windows lock, sleep or sign-out, the lock command, or the
extension shutting down:

1. `BwCli` runs `bw lock`, which makes the session key worthless.
2. `SessionManager` drops the key; `VaultIndex` drops the metadata.
3. `ClipboardService` clears the clipboard if it still holds our last value.

At start-up the extension runs `bw lock` first, so a session left behind by a
crash is closed before anything else.

## Where data lives

| Data | Where | How long |
|---|---|---|
| Master password | Extension memory, then the `bw unlock` child's environment | One unlock call |
| Session key | Extension memory; each `bw` child's environment | Until lock |
| Names, usernames, URIs, "has TOTP" | Extension memory (`VaultIndex`) | Until lock |
| Password, TOTP code | Extension memory, then the clipboard | One copy; clipboard cleared after 20 seconds by default |
| Encrypted vault | `data.json`, owned by `bw` | Until `bw logout` |
| Settings (bw path, timeouts, clear delay) | Command Palette extension settings file | Permanent; never a secret |

Nothing the extension writes to disk contains a secret or a vault item name.
.NET strings cannot be reliably wiped from memory; the design keeps their
lifetime short and does not claim erasure ([ADR 6](adr/0006-threat-model.md)).

## Repository layout

```text
src/BitwardenCommandPalette.Core/         session, vault index, bw, clipboard, secrets
src/BitwardenCommandPalette.Extension/    Command Palette extension, MSIX
tests/BitwardenCommandPalette.Core.Tests/ unit tests, canary tests
tests/FakeBw/                             test stand-in for bw.exe (phase 3)
tests/FakeBw/recordings/                  scrubbed bw output from a test vault
tools/                                    Record-BwOutput.ps1, which makes those recordings
docs/                                     this document, roadmap, pipeline, ADRs
```

## Testing

Tests never run the real `bw` and never touch a real vault. A small fake `bw`
executable replays scripted answers and records how it was called, so tests can
check what was passed in arguments and environment. Canary tests run every flow
with known fake secrets and fail if one appears in a log line, an exception
message or a child's arguments ([ADR 13](adr/0013-secret-types-and-canary-tests.md)).

The fake (`tests/FakeBw`, built as `bw.exe` next to the tests) reads a scenario
file named by `FAKEBW_SCENARIO`: rules that match the exact arguments and answer
with one of the recordings in `tests/FakeBw/recordings/` or a made-up response.
It logs each call's arguments, `BW_SESSION` and the variable named by
`--passwordenv`. Because that variable is process-wide, tests that start it run
one at a time.

`BwCli` behaviour that follows from the recordings ([spike](spike.md)): failures
are classified by bw's known messages into `BwErrorKind`, each with a fixed
message of ours; `NotFound` means the item or that field of it is missing, so
callers decide from `VaultIndex` which actions to offer; item lookups refuse
anything but a GUID; a password is returned exactly as bw prints it, a TOTP code
trimmed; and `bw list items` is read from a `JsonDocument`, so passwords and TOTP
secrets in it are only checked for presence and never become strings of their
own. The raw output string still holds them until it is collected, as ADR 7
accepts.
