# Installing

Bitwarden Command Palette is an extension for the PowerToys Command Palette. It
works through the official Bitwarden CLI, which you install and log in to
yourself.

There are two ways to install it ([ADR 20](adr/0020-distribute-through-the-microsoft-store.md)):

- **Microsoft Store (recommended).** Do steps 1 and 2, then install
  **Bitwarden for Command Palette** from the Microsoft Store. Microsoft signs
  the package and keeps it up to date; there is no certificate to trust.
- **GitHub release.** For checking that the package was built by this
  repository's pipeline. These releases are signed with the project's own
  development certificate, which Windows does not trust by default: steps 3
  to 5. Installing one replaces a Store copy and the other way round.

## 1. Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11, on x64 or ARM64.
- [PowerToys](https://learn.microsoft.com/windows/powertoys/install) with
  Command Palette turned on.
- A Bitwarden account, on bitwarden.com, bitwarden.eu or a self-hosted server.

## 2. Install and log in to the Bitwarden CLI

The extension only runs a `bw.exe` signed by Bitwarden Inc.
([ADR 15](adr/0015-finding-and-verifying-bw.md)), so use the native Windows
build. The npm package is not found.

```bash
winget install Bitwarden.CLI
```

Scoop (`bitwarden-cli`), Chocolatey (`bitwarden-cli`) and the zip from
[Bitwarden's download page](https://bitwarden.com/help/cli/#download-and-install)
work too; for the zip, put `bw.exe` on your `PATH` or set its path in the
extension's settings.

Then log in once in a terminal. The extension never logs in or out, and never
asks for an API key or a two-step code
([ADR 12](adr/0012-the-extension-unlocks-it-never-logs-in.md)).

```bash
bw config server https://vault.bitwarden.eu
```

(Only for the EU cloud or a self-hosted server; leave it out for bitwarden.com.)

```bash
bw login
```

You do not need to run `bw unlock`: the extension unlocks when you use it, and
locks again after 10 minutes without use, or 1 hour at most (both can be
changed in its settings).

## 3. Download and check the package (GitHub release)

From the [latest release](https://github.com/wedsel2/Bitwarden-Command-Palette/releases),
download for your processor (Settings, System, About, System type):

- `BitwardenCommandPalette-<version>-x64.msix`, or
- `BitwardenCommandPalette-<version>-arm64.msix`,

and `BitwardenCommandPalette-signing.cer`.

Check that the package was built by this repository's release workflow from
`main`. With the [GitHub CLI](https://cli.github.com/):

```bash
gh attestation verify BitwardenCommandPalette-<version>-x64.msix --repo wedsel2/Bitwarden-Command-Palette --signer-workflow wedsel2/Bitwarden-Command-Palette/.github/workflows/ci.yml --source-ref refs/heads/main
```

Its output names the commit the package was built from.

Without the GitHub CLI you can only check that the download is intact, not
where it came from: compare the file's hash with the line in the release's
`SHA256SUMS.txt`.

```bash
certutil -hashfile BitwardenCommandPalette-<version>-x64.msix SHA256
```

The release also carries a CycloneDX SBOM (`.cdx.json`): every package the
extension is built from, with its version and licence.

## 4. Trust the development certificate

This step needs an **administrator** PowerShell: open the Start menu, type
`PowerShell`, right-click **Windows PowerShell** and choose **Run as
administrator**, then go to the folder you downloaded to:

```powershell
cd $env:USERPROFILE\Downloads
```

Check the certificate's thumbprint:

```powershell
(Get-PfxCertificate .\BitwardenCommandPalette-signing.cer).Thumbprint
```

It must be exactly:

```text
ED72DA0FF1331A20F0DD1F8182B8FF9F935E7CF5
```

(Subject `CN=6FAB27EF-B582-4420-BFED-78BE0A44207B`, the publisher ID the
Microsoft Store assigned to this project; valid until 8 November 2029.)

If it differs, stop: the file is not this project's. If it matches, add it to
the machine's **Trusted People** store, in the same administrator window (in a
normal window this fails with "Access is denied"):

```powershell
Import-Certificate -FilePath .\BitwardenCommandPalette-signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Trusted People makes Windows trust anything signed with this certificate's key,
not only this package: other programs and scripts too. It does not make it a
certificate authority, so it cannot vouch for websites or for other
certificates. A self-signed certificate cannot be revoked, so if its key were
ever leaked, the project would announce it in a release and on the
[releases page](https://github.com/wedsel2/Bitwarden-Command-Palette/releases),
and you would remove the certificate as under [Uninstalling](#uninstalling).
This is why the Microsoft Store is the recommended way to install.

## 5. Install

Double-click the `.msix` and choose **Install**, or in PowerShell (a normal
window is enough):

```powershell
Add-AppxPackage .\BitwardenCommandPalette-<version>-x64.msix
```

Open Command Palette and type `bitwarden`. Choose **Bitwarden**, enter your
master password, and search your logins. The extension's settings are in
Command Palette's settings, under **Extensions**, **Bitwarden**.

If the Bitwarden commands show up twice after installing over an earlier
version, run **Reload Command Palette extensions** in Command Palette, or exit
PowerToys from its tray icon and start it again.

## Updating

Download the new release, check it as in step 3, and install it over the old
one as in step 5. The certificate stays trusted until it expires.

## Uninstalling

Remove **Bitwarden for Command Palette** in Settings, Apps, Installed apps, or:

```powershell
Get-AppxPackage wedsel2.BitwardenforCommandPalette | Remove-AppxPackage
```

If you installed a GitHub release, also remove the certificate, again as
administrator:

```powershell
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=6FAB27EF-B582-4420-BFED-78BE0A44207B' | Remove-Item
```

Earlier pre-releases used the package name `wedsel2.BitwardenCommandPalette`
and the subject `CN=wedsel2`; remove those the same way if you still have them.

The extension keeps no vault data on disk; its settings file goes with the
package. The Bitwarden CLI keeps its own data; `bw logout` removes your
account from it.
