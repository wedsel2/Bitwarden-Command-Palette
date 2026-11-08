# 19. Website icons come from the vault's Bitwarden icon server and stay in memory

- Status: Accepted
- Date: 2026-11-08

## Context

A list of logins with the same key icon is slow to scan. Bitwarden's own apps
show each login's website icon, fetched from Bitwarden's icon service
(`https://icons.bitwarden.net/<host>/icon.png`, or the matching server of the
EU cloud or a self-hosted install), with a setting "Show website icons" that is
on by default.

The vault is end-to-end encrypted, so Bitwarden's servers cannot read which
websites a user has logins for. An icon request tells the icon server that
host in plain text. Bitwarden says the icon service does not link requests to
accounts; the request travels over HTTPS, so the network only sees the icon
server's name. No password, username or item name is involved.

Command Palette accepts an icon as a file path, a URL, a glyph or a stream. A
URL would make Command Palette fetch and cache the icon itself, outside our
control, possibly on disk.

## Decision

- A setting **Show website icons**, on by default as in Bitwarden's apps. Off,
  no icon request is made and the cached icons are dropped.
- The icon server is the one of the vault's server, from `bw status`:
  `icons.bitwarden.net` for the US cloud (no server set, or `bitwarden.com`),
  `icons.bitwarden.eu` for the EU cloud, and `<server>/icons/` for a
  self-hosted server. A self-hosted server not on HTTPS gets no icons.
- Only a login's first website is asked for, by its host name alone: never a
  path, query, port or user info. Hosts that only make sense on a private
  network are never sent: IP addresses, names without a dot, and the
  `localhost`, `local`, `internal`, `lan`, `home.arpa`, `test`, `example` and
  `invalid` domains.
- `WebsiteIcons` in Core fetches with its own `HttpClient`: HTTPS only, no
  redirects, no cookies, a 10-second timeout, at most 4 requests at once. A
  response is kept only if it is `200`, at most 64 KB, and starts like a PNG,
  JPEG, GIF, ICO, BMP or WebP image.
- Icons are kept **in memory only**, one fetch per host, and dropped at every
  lock. The extension hands them to Command Palette as an in-memory stream,
  never as a URL or file.
- A failed or rejected fetch shows the key icon, and is not retried until the
  next unlock.

## Alternatives considered

- **Off by default**: keeps hosts off Bitwarden's servers, but Bitwarden's own
  apps already make this trade by default, and the user chose to match them.
- **Hand Command Palette the icon URL**: simplest, but Command Palette's own
  image cache could write the list of a user's websites to disk (ADR 6, item 2).
- **Fetch from the website itself or a third-party favicon service**: tells
  every website, or another company, which sites the user has logins for.
- **No icons**: safest, and the list stays hard to scan.

## Consequences

- With the setting on, Bitwarden's icon server learns the hosts of the user's
  logins, as with Bitwarden's own apps. This is new exposure to a party ADR 6
  already trusts with the encrypted vault; it weakens none of ADR 6's seven
  protections, and the README states it with the setting.
- The extension now makes network requests of its own, not only through `bw`.
- Icons appear a moment after the list; each host is fetched once per unlock.
- Whether Command Palette keeps a stream icon only in memory is checked in the
  phase 4 live test.
