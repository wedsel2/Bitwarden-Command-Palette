# 8. No IPC of our own: Core and Extension as two assemblies in one process

- Status: Accepted
- Date: 2026-11-07

## Context

The original notes separate a lightweight palette UI from a backend that holds
session and vault logic, connected by a named pipe or localhost socket. The
aim is to keep sensitive code apart from the UI and easy to test. Command
Palette, however, already runs each extension in its own process and talks to
it over WinRT/COM; it never loads extension code into its own process.

## Decision

- No named pipe, socket or other IPC of our own.
- The separation is between assemblies in the extension process:
  `BitwardenCommandPalette.Core` holds all session, vault, `bw` and clipboard
  logic and references no Command Palette or UI package;
  `BitwardenCommandPalette.Extension` holds pages and commands and calls Core
  only through its public interfaces.
- Core is fully covered by unit tests without Command Palette.

## Alternatives considered

- **Separate backend process with a named pipe**: another endpoint that every
  same-user process could try to connect to, requiring its own authentication
  and access control, to separate code that already runs outside the host.
- **Localhost socket**: the same, and also reachable from browsers through
  DNS rebinding unless carefully protected.

## Consequences

- One process to reason about, and no extra attack surface.
- A bug in the extension layer runs in the same process as the session key;
  keeping that layer thin and free of Bitwarden logic limits what it can do.
- If a future feature needs a second process, it needs a new record.
