# Bitwarden Command Palette

PowerToys Command Palette extension that searches Bitwarden logins by name and
copies a username, password or TOTP code through the `bw` CLI. Security over
convenience; open-source, pinned dependencies only.

Read before designing anything:

- `docs/architecture.md` - components, flows, where data lives
- `docs/roadmap.md` - phases and what is in scope now
- `docs/adr/` - why things are the way they are; ADR 6 is the threat model
- `docs/ci-pipeline.md` - pipeline, gates, releasing

## Commands

```bash
dotnet tool restore
dotnet build
dotnet test --coverlet --coverlet-threshold 80
dotnet format BitwardenCommandPalette.slnx
dotnet nuget-license -i BitwardenCommandPalette.slnx -t -a allowed-licenses.json -override license-overrides.json -ignore license-exceptions.json
uvx pre-commit run --all-files   # everything CI lint runs
dotnet build src/BitwardenCommandPalette.Extension -c Debug -p:Platform=x64   # loose MSIX layout for a local test
```

Needs Windows, the .NET SDK version in `global.json` and the Windows SDK
10.0.26100 (for the CsWinRT projection and MSIX tooling).

## Workflow

- `main` is protected. Work on a branch, open a pull request, squash-merge.
- Commit messages and PR titles follow Conventional Commits. `feat:` and `fix:`
  trigger a release; `docs:`, `ci:`, `chore:`, `test:`, `refactor:` do not.
- Add a package by adding its version to `Directory.Packages.props` and a
  reference without a version to the project, then `dotnet restore`; commit the
  updated `packages.lock.json` files. Never edit a lock file by hand.
- A new package must pass the licence check. Never add to
  `allowed-licenses.json` or `license-exceptions.json` without asking (ADR 14).
  An override in `license-overrides.json` is only for an open-source licence
  the checker cannot read, after reading the licence file of that exact version.
- Pin GitHub Actions by commit SHA with the version in a trailing comment.

## Pull requests

Before telling the user a pull request is ready to merge:

1. Run `/code-review` on the branch and fix or report what it finds.
2. Run `/security-review` when the change touches `BwCli`, `SessionManager`,
   `ClipboardService`, the secret types, logging, process start-up, settings,
   packaging or the workflows. Skip it for documentation-only changes and say
   that you skipped it.
3. Turn on Auto-fix for the pull request when the session offers it, so failing
   checks come back to the session. Never turn on auto-merge unless the user asks.
4. In the summary to the user, say which of these ran and what they found.

## Rules

- **Never run `bw unlock`, `bw get`, `bw list` or `bw export` against a real
  vault**, and never ask the user for a master password, session key or API
  key. The spike records CLI output from a throwaway test vault, which the user
  runs; recordings are scrubbed before they are committed.
- **Only `BwCli` starts a process.** Other code goes through Core's interfaces.
- **No secret in a process argument.** The master password goes through
  `--passwordenv`, the session key through `BW_SESSION`, both on the child's
  environment only, never on ours (ADR 7).
- **Every secret is a secret type** (`MasterPassword`, `SessionKey`, `Secret`).
  Never put a raw secret in a `string` field, a record, a log call or an
  exception message. Never log `bw` output verbatim (ADR 13).
- **Nothing secret touches disk**: no cache, no Credential Manager, no temp
  file, no settings value. Vault item names count as secret (ADR 9, 10).
- **Every clipboard write goes through `ClipboardService`** (ADR 11).
- **Tests never run the real `bw` or contact Bitwarden.** They use the fake `bw`
  in `tests/FakeBw/`. New behaviour that handles a secret gets a canary test.
- **Core references no UI or Command Palette package** (ADR 8).
- **This repository is public.** Never commit vault data, recordings that were
  not scrubbed, signing certificates or credentials.
- **An architecture decision needs a record.** Use the `adr` skill, and update
  `docs/architecture.md` in the same pull request when the design changes. A
  change that weakens the threat model in ADR 6 needs a new record first.
- New behaviour comes with tests; the build has no warnings.
