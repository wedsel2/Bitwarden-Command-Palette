# 13. Secrets are types that cannot be printed, and canary tests prove nothing leaks

- Status: Accepted
- Date: 2026-11-07

## Context

The original notes require that passwords and session keys never appear in
logs, crash output or error messages, and that this is tested in the pipeline.
A rule that depends on every developer remembering it fails eventually; it has
to be enforced by types and checked by tests.

## Decision

- Every secret is wrapped in a type: `MasterPassword`, `SessionKey` and
  `Secret` (password, TOTP code, copied value). Their `ToString()`, debugger
  display and any serialisation return `***` (`Redacted.Placeholder`). The raw
  value is only reachable through a method whose name says so, used by `BwCli`
  and `ClipboardService`.
- Logging goes through one wrapper. Secret types are never passed to it, and
  `bw` output is never logged verbatim.
- Exceptions from `BwCli` carry our own messages, never `bw` stderr or stdout.
- **Fake `bw`**: tests run against a small test executable that replays
  scripted answers (recorded from a test vault in the spike, then scrubbed) and
  records the arguments and environment it received. Tests and CI never run the
  real `bw` or contact a Bitwarden server.
- **Canary tests** run unlock, search, every copy action, timeouts, lock and
  every error path with known fake secrets, capture all log output and
  exception messages, and fail if a canary appears. A test fails if any child
  process received a secret in its arguments.

## Alternatives considered

- **Code review only**: misses the indirect cases (an exception message that
  includes a command line, a record type's generated `ToString()`).
- **`SecureString`**: deprecated by Microsoft for new code; it does not protect
  the value once converted, which `bw` and the clipboard both require.
- **Recording the real `bw` in CI**: needs a vault and credentials in CI.

## Consequences

- Slightly more ceremony around every secret, by design.
- Records holding a secret must use the secret type, or their generated
  `ToString()` would print it; the canary tests catch a mistake.
- The fake `bw` must be kept in step with the real CLI's output; the spike's
  recordings are the reference.
