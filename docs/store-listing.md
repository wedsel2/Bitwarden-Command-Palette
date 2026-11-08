# Microsoft Store listing

The text for the Partner Center submission ([ADR 20](adr/0020-distribute-through-the-microsoft-store.md)).
Kept here so changes are reviewed like code.

## Properties

- **Category**: Productivity (subcategory: none).
- **Privacy policy URL**:
  `https://github.com/wedsel2/Bitwarden-Command-Palette/blob/main/docs/privacy.md`
- **Website**: `https://github.com/wedsel2/Bitwarden-Command-Palette`
- **Support contact**: `https://github.com/wedsel2/Bitwarden-Command-Palette/issues`
- **System requirements**: Windows 10 version 2004 (build 19041) or later;
  PowerToys with Command Palette.

## Description

> Bitwarden for Command Palette integrates with the Windows Command Palette to
> search your Bitwarden vault and copy a username, password or TOTP code
> without leaving the keyboard.
>
> Type "bitwarden", unlock with your master password, and search your logins
> by name, website or username. Copied secrets are kept out of clipboard
> history and cleared after a few seconds. The vault locks after inactivity
> and when Windows locks or sleeps.
>
> Requires PowerToys with Command Palette, and the official Bitwarden CLI
> (`winget install Bitwarden.CLI`), logged in once with `bw login`.
>
> Open source and not affiliated with Bitwarden Inc. Bitwarden is a trademark
> of Bitwarden Inc.

## Features

- Search Bitwarden logins from Command Palette
- Copy username, password or TOTP code
- Master password reprompt for protected logins
- Clipboard cleared automatically, kept out of clipboard history
- Locks after inactivity and when Windows locks
- Website icons, kept in memory only

## Search terms

Bitwarden, password manager, Command Palette, PowerToys, TOTP, 2FA, passwords

## Additional testing information

> This is an extension for the PowerToys Command Palette; it has no window of
> its own.
>
> 1. Install PowerToys (`winget install Microsoft.PowerToys`) and turn on
>    Command Palette in PowerToys Settings.
> 2. Install the Bitwarden CLI (`winget install Bitwarden.CLI`).
> 3. Create a free account at https://vault.bitwarden.com, add a login or two,
>    and in a terminal run `bw login` with that account.
> 4. Open Command Palette (Win+Alt+Space), type "bitwarden", select
>    "Bitwarden", enter the master password, and pick a login to copy its
>    username or password.
>
> The extension never logs in by itself; without step 3 it shows how to log in.

## Screenshots

At least one, 1366×768 or larger, of Command Palette showing the extension.
Use the test vault's `BWCP Fixture` logins only, never a real vault.
