# Bitwarden Command Palette

A [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/overview)
extension that searches your Bitwarden vault by name and copies a username,
password or TOTP code, through the official Bitwarden CLI.

<a href="https://apps.microsoft.com/detail/9p4x8z459n7r?referrer=appbadge">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://get.microsoft.com/images/en-us%20light.svg">
    <img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200" alt="Get it from Microsoft">
  </picture>
</a>

**Status: pre-release.** Install it from the Microsoft Store, which signs it, so there is no certificate to trust; GitHub releases, signed with the
project's own development certificate, are there to verify the build. See the
[install guide](docs/install.md), the [roadmap](docs/roadmap.md) and the
[architecture](docs/architecture.md).

Not affiliated with or endorsed by Bitwarden Inc.

## What it does

- Type `bitwarden` in Command Palette, unlock with your master password, and
  search your logins by name, website or username.
- Copy a username, password or TOTP code. Logins marked for master password
  reprompt ask for it again before a password or TOTP copy.
- Lock the vault, or sync it with your server, from Command Palette.
- Shows each login's website icon from Bitwarden's icon service, kept in memory
  only; you can turn this off.

You log in once with `bw login` in a terminal; the extension only unlocks.

## Security

Security comes before convenience. The full reasoning is in the
[threat model](docs/adr/0006-threat-model.md) and the
[decision records](docs/adr/README.md).

**What it protects against:**

1. Secrets in our logs, exception messages or crash output.
2. Secrets on disk: no cache, no Credential Manager entry, no temporary file,
   no settings value. Vault item names count as secret.
3. Secrets on a process command line: the master password and session key go
   to `bw` through its environment only.
4. A vault left unlocked: it locks after inactivity (10 minutes by default),
   always after a fixed time (1 hour by default), and when Windows locks,
   sleeps, signs out or turns the display off.
5. Secrets kept by the clipboard: copies stay out of clipboard history and the
   cloud clipboard, and are cleared after 20 seconds by default if still there.
6. A wrong or tampered `bw.exe`: only a `bw.exe` signed by Bitwarden Inc. runs.
7. A compromised dependency or build: every package is open source and pinned,
   checked for licences and known vulnerabilities; releases carry an SBOM and a
   build provenance attestation.

**What it does not protect against:**

- **Malware running as your Windows user.** It can read the extension's
  memory, the clipboard and your keystrokes. No extension can stop that.
- **Secrets lingering in memory.** .NET cannot reliably wipe strings; the
  design keeps secrets short-lived but does not claim to erase them.
- **A compromised Bitwarden account or server**, or a flaw in `bw` itself.
- **Command Palette itself**: your master password passes through its process
  on the way to the extension.

## Development

Needs Windows, the .NET SDK version in `global.json` and the Windows SDK
10.0.26100.

```bash
dotnet tool restore
dotnet build
dotnet test --coverlet --coverlet-threshold 80
```

Every change passes an automated pipeline: build with all analyzers as errors,
tests with coverage, vulnerability and licence checks. See
[the pipeline](docs/ci-pipeline.md).

## Licence

MIT. The Bitwarden CLI, which this extension runs as a separate program, is
GPL-3.0 and installed separately. Bitwarden is a trademark of Bitwarden Inc.
