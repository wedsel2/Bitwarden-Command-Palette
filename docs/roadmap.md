# Roadmap

Each phase ends in something that works and is merged. Later phases are
intentionally less detailed.

| # | Phase | Outcome | Status |
|---|---|---|---|
| 0 | Pipeline | Solution skeleton, CI with all gates, Renovate, semantic-release | Done |
| 1 | Design | Architecture doc, threat model and decision records, `CLAUDE.md`, roadmap | Done |
| 2 | Spike | A hello-world extension installed in Command Palette; the questions below answered; [findings](spike.md) written; Proposed records settled | Done: extension builds, CLI checked and recorded, live test answered questions 3 and 4; the low-priority open items in the findings stay open |
| 3 | Core | Remove the spike diagnostics (`SpikeLog`, `Microsoft.Win32.SystemEvents` unless lock events need it); `BwCli`, `SessionManager` with timers and lock events, `VaultIndex`, `ClipboardService`, secret types, fake `bw`, canary tests | Done: secret types, fake `bw`, `BwCli`, canary tests, `bw.exe` discovery and signature check, `VaultIndex`, `ClipboardService`, `SessionManager`; spike diagnostics removed, Core owns the lock events |
| 4 | Extension | Unlock form, search list, per-login actions (copy username, password or TOTP), lock command, settings page | In progress: unlock form, search list, copy actions and lock command; next the settings page |
| 5 | Release | Signed MSIX with SBOM and provenance on GitHub Releases, install guide, README with the threat model | |
| 6 | Later | Each needs a decision record first: open URI, WinGet listing, password generation, Windows Hello | |

## Phase 2 questions

Command Palette

1. Template, target framework and Windows SDK version of a current extension;
   does it build in CI on `windows-latest` and does `dotnet format` accept it?
2. Can a form page take a password field, and how does the value reach the
   extension (does Command Palette keep a copy)?
3. How long does the extension process live? Does it get a shutdown call we can
   lock in?
4. Do `SystemEvents.SessionSwitch` and `PowerModeChanged` fire inside the
   extension process, or is `WTSRegisterSessionNotification` needed?
5. Where does the settings toolkit store values?

Bitwarden CLI

6. Exact JSON of `bw status`, `bw unlock --raw`, `bw list items`, `bw get`
   for a test vault, to build the fake `bw` from.
7. Does `--passwordenv` work on the current release, and does `BW_SESSION`
   in the child environment cover every command we use?
8. Is the native Windows `bw.exe` Authenticode-signed by Bitwarden Inc, and
   from which install routes (GitHub release zip, winget, Chocolatey)?
9. What does `bw get totp` return for an item without TOTP, or without Premium?
10. Can the CLI read the account's password generator settings (for phase 6)?

Prior art

11. How does the community "Bitwarden for Command Palette" extension handle
    sessions and the clipboard, and what should this one do differently?

## Deferred on purpose

- **Windows Hello unlock.** It means storing something derived from the master
  password behind Hello, which changes the trust model. Needs its own record.
- **Password generation.** Must use Bitwarden's settings, not ours; depends on
  question 10.
- **Bitwarden SDK instead of the CLI.** No supported .NET binding; revisit if
  Bitwarden publishes a stable one ([ADR 7](adr/0007-bitwarden-access-through-the-cli.md)).
- **Items other than logins** (cards, identities, notes).
