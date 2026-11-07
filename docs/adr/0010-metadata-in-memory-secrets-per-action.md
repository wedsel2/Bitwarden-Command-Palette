# 10. Login metadata in memory only; passwords and TOTP fetched per action

- Status: Accepted
- Date: 2026-11-07

## Context

Search has to be instant, so it cannot start a `bw` process per keystroke. The
original notes propose loading only metadata ahead of time and fetching secrets
when they are used, and leave open how often to sync and how to protect a
local cache of names, which can themselves be sensitive.

## Decision

- At unlock, `VaultIndex` loads the logins from `bw list items` and keeps only
  ID, name, username, URIs, folder and whether a TOTP seed exists. Every other
  field is discarded as soon as the output is parsed.
- The index lives **in memory only** and is dropped at lock. There is no cache
  on disk, so there is nothing to protect there.
- **Sync**: `bw sync` runs at unlock when the last sync is older than 15
  minutes, and on request through a "Sync vault" command.
- Password and TOTP code are fetched with `bw get password <id>` or
  `bw get totp <id>` **only when the user picks that action**, and handed
  straight to the clipboard.
- The TOTP action is shown only for logins whose index entry says a seed
  exists; the seed itself is never kept.

## Alternatives considered

- **Encrypted cache on disk** for faster start-up: a new secret to manage, and
  names on disk even when locked.
- **Calling `bw list items --search` per query**: a process start and a full
  decryption per keystroke, and all matching passwords in our process each time.
- **Keeping whole items in memory**: every password in memory for the whole
  session instead of for one copy.

## Consequences

- Unlocking takes as long as `bw list items` on the vault, every time.
- Passwords of all logins pass through our process once per unlock, as parsed
  JSON, before being discarded ([ADR 7](0007-bitwarden-access-through-the-cli.md)).
- Changes made in other Bitwarden clients appear after the next unlock or a
  manual sync.
