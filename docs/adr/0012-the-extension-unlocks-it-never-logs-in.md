# 12. The extension unlocks; logging in stays with the user

- Status: Accepted
- Date: 2026-11-07

## Context

The Bitwarden CLI distinguishes logging in (`bw login`: authenticating to the
server, with two-step login, possibly new-device verification, or an API key)
from unlocking (`bw unlock`: decrypting the local vault with the master
password). The original notes list the API key as an alternative to the master
password, but `bw login --apikey` only logs in; `bw unlock` still needs the
master password.

## Decision

- The extension never runs `bw login` or `bw logout`, and never asks for an API
  key, a two-step code or a server URL.
- The user logs in once in a terminal with `bw login` (and `bw config server`
  for a self-hosted server). The extension checks `bw status` and, when the CLI
  is not logged in, explains this step instead of offering a login form.
- The extension only unlocks, lists, gets, syncs and locks.

## Alternatives considered

- **Login form in the extension**: master password, two-step codes, email
  verification and API keys all pass through our code, for a step that happens
  once per device.
- **API key login**: does not remove the master password, and adds a long-lived
  credential to store.

## Consequences

- Much less sensitive code and far fewer flows to test.
- One manual step on first use, which the install guide and the extension's
  message must explain clearly.
- If the server logs the CLI out, the user repeats `bw login` in a terminal.
