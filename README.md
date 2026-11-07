# Bitwarden Command Palette

A [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/overview)
extension that searches your Bitwarden vault by name and copies a username,
password or TOTP code, through the official Bitwarden CLI.

**Status: in development, nothing to install yet.** See the [plan](docs/plan.md).

## Principles

- **Security over convenience.** The vault locks after inactivity, the session
  key lives only in memory, and copied secrets are kept out of clipboard history
  and cleared after a few seconds.
- **Traceable.** Every dependency is open source and pinned in a lock file, and
  every change passes an automated pipeline: build with all analyzers as errors,
  tests with coverage, vulnerability and licence checks. See
  [the pipeline](docs/ci-pipeline.md).

## Development

Needs Windows and the .NET SDK version in `global.json`.

```bash
dotnet tool restore
dotnet build
dotnet test --coverlet --coverlet-threshold 80
```

## Licence

MIT. The Bitwarden CLI, which this extension runs as a separate program, is
GPL-3.0 and installed separately.
