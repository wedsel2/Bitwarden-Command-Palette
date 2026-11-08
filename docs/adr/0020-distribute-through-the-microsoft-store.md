# 20. Distribute through the Microsoft Store; GitHub releases stay as the verifiable copy

- Status: Accepted
- Date: 2026-11-08

## Context

[ADR 5](0005-releases-signed-msix.md) planned to sign releases through SignPath
Foundation, with a self-signed development certificate until then. Users of a
development-signed MSIX must check a thumbprint and add the certificate to
`LocalMachine\TrustedPeople` as administrator, which also makes Windows trust
anything else signed with that key. Most users should not have to do that.

What is available to this project in November 2026:

- **Microsoft Store**: free registration for individual developers. The Store
  certifies the package and signs it with Microsoft's certificate, updates it
  automatically, and Command Palette finds extensions installed from it.
- **SignPath Foundation**: free for open source, but asks for an existing
  release and a "verifiable reputation", which a new project does not have yet.
- **Azure Artifact Signing**: public-trust validation for individuals only in
  the USA and Canada; in the EU only for organisations.

## Decision

- The **Microsoft Store** is the recommended way to install. The listing is
  free, and what is uploaded is the bundle CI built and attested.
- The manifest uses the **Store's identity**: the package name and publisher
  that Partner Center reserved. There is one package identity, so a Store copy
  and a GitHub copy replace each other instead of installing side by side.
- The release job keeps doing what ADR 5 set out: semantic-release, packages
  built in the same run as the checks, a CycloneDX SBOM and a build provenance
  attestation. It also builds an **MSIX bundle** (x64 and ARM64) for the Store.
- **GitHub releases stay**, signed with the development certificate, whose
  subject is now the Store publisher. They are for users who want to verify
  the build with `gh attestation verify`, and they remain pre-releases.
- The first Store submission, and each update until it is automated, is a
  manual upload in Partner Center of the bundle attached to the GitHub release,
  so the Store gets exactly the attested file.
- A **privacy policy** is published in the repository (`docs/privacy.md`), as
  the Store requires for an app that handles passwords.
- **SignPath** is dropped as the next step; it can be reconsidered when the
  project has a release history and users.

## Alternatives considered

- **SignPath first** (ADR 5): the right fit later, but a new project would most
  likely be refused, and every user would keep trusting a certificate by hand
  until then.
- **Azure Artifact Signing**: not available to an individual in the
  Netherlands.
- **WinGet with an unpackaged EXE installer** (Microsoft's other guide): no
  certificate to trust, but an unsigned installer that registers the COM server
  in the registry is weaker than a signed MSIX, and loses package identity.
- **Two identities** (Store and GitHub): users could install both side by side,
  and Command Palette would show the extension twice.

## Consequences

- Store users install with one click, get updates, and trust nothing by hand.
- **This narrows ADR 6 item 7 for Store installs** (the GitHub release keeps
  full verification):
  - The Store re-signs the bundle, so the file a Store user gets does not match
    the provenance attestation byte for byte; a Store install trusts
    Microsoft's certification and re-signing.
  - The **Partner Center account is a second way to ship**: whoever can sign in
    to it can upload any package under this identity, and the Store pushes it
    to every install, bypassing branch protection, CI, the SBOM and the
    attestation. To limit this, the account uses physical or passkey MFA and is
    the maintainer's alone, and each upload is the bundle from the GitHub
    release with its SHA-256 checked against that release's `SHA256SUMS.txt`
    first (`docs/ci-pipeline.md`).
  - Uploading from CI through the Store submission API would tie the Store
    copy to CI again; that is the follow-up that closes this gap.
- Store certification adds a review step, can reject or delay an update, and
  the listing must describe the Bitwarden CLI dependency for testers.
- The development certificate must be recreated with the Store publisher as
  its subject (`tools/New-DevSigningCertificate.ps1`), and its new thumbprint
  goes into the install guide.
- Updates are uploaded by hand until the Store submission API is wired into
  the release job; that automation needs its own change.
