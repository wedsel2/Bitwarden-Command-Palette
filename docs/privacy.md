# Privacy policy

Bitwarden for Command Palette ("the extension") is an open-source extension for
the PowerToys Command Palette. This policy covers the extension only.

Last updated: 8 November 2026.

## What we collect

Nothing. The extension has no account, no analytics, no telemetry and no crash
reporting. Its author receives no data from it.

## What stays on your computer

- **Your vault** is read through the Bitwarden CLI (`bw`), which you install
  and log in to yourself. The extension keeps the list of logins in memory
  while the vault is unlocked and drops it when the vault locks.
- **Your master password** is passed to `bw` to unlock the vault, through the
  environment of the `bw` process only. It is never stored.
- **Copied usernames, passwords and TOTP codes** go to the Windows clipboard,
  excluded from clipboard history and cloud clipboard, and are cleared after a
  short time if they are still there.
- **Settings** (path to `bw.exe`, timeouts, clipboard delay, sync interval,
  website icons on or off) are stored in the extension's own app data. They
  contain no password, vault item or website name.

Nothing from your vault is written to disk by the extension.

## What leaves your computer

- **Bitwarden.** `bw` talks to your Bitwarden server (bitwarden.com,
  bitwarden.eu or your own) to unlock and sync your vault. That traffic is
  between `bw` and Bitwarden, under
  [Bitwarden's privacy policy](https://bitwarden.com/privacy/).
- **Website icons.** When "Show website icons" is on (the default, as in
  Bitwarden's own apps), the extension asks your Bitwarden server's icon
  service for the icon of each login's website, by host name only (for example
  `github.com`). Bitwarden can therefore see which websites you have logins
  for, but not your usernames or passwords. Private network names and IP
  addresses are never sent. Turn the setting off to send nothing.

Nothing is sent to the extension's author or to any other party.

## Children

The extension is a tool for Bitwarden users and does not target children.

## Changes and contact

Changes to this policy are made in this file, so the
[history](https://github.com/wedsel2/Bitwarden-Command-Palette/commits/main/docs/privacy.md)
shows every version. Questions:
[open an issue](https://github.com/wedsel2/Bitwarden-Command-Palette/issues).
