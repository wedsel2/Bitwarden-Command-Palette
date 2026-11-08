<#
.SYNOPSIS
Creates the self-signed development certificate that signs releases until SignPath does (ADR 5),
and stores it as the GitHub Actions secrets DEV_SIGNING_CERTIFICATE and DEV_SIGNING_CERTIFICATE_PASSWORD.

.DESCRIPTION
The maintainer runs this once, and again when the certificate expires or must be replaced.
Needs the GitHub CLI (gh), signed in with rights to set repository secrets.

The private key exists only in the GitHub secret: the PFX is written to a temporary file, sent to
gh on standard input (never as an argument), and deleted; the certificate is removed from the
local store again. The password is random and shown nowhere.

Prints the thumbprint, which goes into docs/install.md: releases attach the public certificate,
and users compare its thumbprint with the one there before trusting it.

.PARAMETER Repository
The repository whose secrets are set, as owner/name.

.PARAMETER Years
How long the certificate is valid.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Repository = 'wedsel2/Bitwarden-Command-Palette',
    [ValidateRange(1, 5)] [int] $Years = 3
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'The GitHub CLI (gh) is needed to set the secrets.'
}

# Must match the Publisher in the manifest, which is the Store's publisher (ADR 20).
$manifestPath = Join-Path $PSScriptRoot '..\src\BitwardenCommandPalette.Extension\Package.appxmanifest'
[xml] $manifest = Get-Content $manifestPath -Raw
$subject = $manifest.Package.Identity.Publisher
if (-not $subject) {
    throw "No Publisher found in $manifestPath."
}

if (-not $PSCmdlet.ShouldProcess($Repository, "Create a code signing certificate for $subject and set its secrets")) {
    return
}

$certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
    -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy Exportable `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears($Years) `
    -TextExtension @('2.5.29.19={text}false') # basic constraints: not a CA

$pfx = Join-Path ([IO.Path]::GetTempPath()) "$([guid]::NewGuid()).pfx"
try {
    $bytes = [byte[]]::new(32)
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    $random.GetBytes($bytes)
    $random.Dispose()
    $plain = [Convert]::ToBase64String($bytes)
    $password = ConvertTo-SecureString $plain -AsPlainText -Force

    Export-PfxCertificate -Cert $certificate -FilePath $pfx -Password $password | Out-Null

    [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx)) | gh secret set DEV_SIGNING_CERTIFICATE --repo $Repository
    if ($LASTEXITCODE -ne 0) { throw 'Setting DEV_SIGNING_CERTIFICATE failed.' }

    $plain | gh secret set DEV_SIGNING_CERTIFICATE_PASSWORD --repo $Repository
    if ($LASTEXITCODE -ne 0) { throw 'Setting DEV_SIGNING_CERTIFICATE_PASSWORD failed.' }
}
finally {
    Remove-Item $pfx -Force -ErrorAction SilentlyContinue
    Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -DeleteKey -ErrorAction SilentlyContinue
}

"Secrets set on $Repository. Certificate thumbprint: $($certificate.Thumbprint), valid until $($certificate.NotAfter.ToString('yyyy-MM-dd'))."
