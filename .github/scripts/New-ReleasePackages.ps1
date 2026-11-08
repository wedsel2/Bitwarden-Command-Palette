<#
.SYNOPSIS
Builds the signed release packages: one MSIX per platform, a CycloneDX SBOM and SHA-256 sums.

.DESCRIPTION
Run by the package job in .github/workflows/ci.yml, and locally to try a release build.
The signing certificate must already be in Cert:\CurrentUser\My, with its private key; the
MSIX tooling signs from the store by thumbprint, so no password appears on a command line.
Its subject must match the Publisher in Package.appxmanifest. See docs/ci-pipeline.md.

.PARAMETER Version
The release version (semantic, x.y.z). The packages get x.y.z.0.

.PARAMETER CertificateThumbprint
Thumbprint of the signing certificate in Cert:\CurrentUser\My.

.PARAMETER OutDir
Where the release files are written; created if needed, and must be empty.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+$')] [string] $Version,
    [Parameter(Mandatory)] [ValidatePattern('^[0-9A-Fa-f]{40}$')] [string] $CertificateThumbprint,
    [Parameter(Mandatory)] [string] $OutDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $root 'src\BitwardenCommandPalette.Extension\BitwardenCommandPalette.Extension.csproj'
$manifestPath = Join-Path $root 'src\BitwardenCommandPalette.Extension\Package.appxmanifest'
$timestampServer = 'http://timestamp.digicert.com'

$certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint"
if (-not $certificate.HasPrivateKey) {
    throw 'The signing certificate has no private key in the store.'
}

[xml] $manifest = Get-Content $manifestPath -Raw
if ($manifest.Package.Identity.Publisher -ne $certificate.Subject) {
    throw "The certificate subject '$($certificate.Subject)' does not match the manifest Publisher '$($manifest.Package.Identity.Publisher)'."
}

New-Item $OutDir -ItemType Directory -Force | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
if (Get-ChildItem $OutDir) {
    throw "$OutDir is not empty."
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "bwcp-packages-$([guid]::NewGuid())"
$original = [IO.File]::ReadAllBytes($manifestPath)
try {
    # The version lives in the manifest; stamped for this build and restored afterwards.
    $manifest.Package.Identity.Version = "$Version.0"
    $manifest.Save($manifestPath)

    foreach ($platform in 'x64', 'ARM64') {
        dotnet publish $project -c Release "-p:Platform=$platform" `
            -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never `
            -p:AppxPackageSigningEnabled=true `
            "-p:PackageCertificateThumbprint=$CertificateThumbprint" `
            "-p:AppxPackageSigningTimestampServerUrl=$timestampServer" `
            -p:AppxPackageSigningTimestampDigestAlgorithm=SHA256 `
            "-p:AppxPackageDir=$staging\$platform\"
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish for $platform failed."
        }

        $msix = @(Get-ChildItem (Join-Path $staging $platform) -Recurse -Filter '*.msix')
        if ($msix.Count -ne 1) {
            throw "Expected one MSIX for $platform, found $($msix.Count)."
        }

        $target = Join-Path $OutDir "BitwardenCommandPalette-$Version-$($platform.ToLowerInvariant()).msix"
        Copy-Item $msix[0].FullName $target
        $signature = Get-AuthenticodeSignature $target
        if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
            throw "$target is not signed with the release certificate."
        }
    }
}
finally {
    [IO.File]::WriteAllBytes($manifestPath, $original)
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}

# The SBOM covers the extension and Core with their packages; build-only tools are listed with
# scope "excluded", the tests not at all.
dotnet CycloneDX $project --recursive --output $OutDir `
    --filename "BitwardenCommandPalette-$Version.cdx.json" --output-format Json `
    --set-name BitwardenCommandPalette --set-version $Version
if ($LASTEXITCODE -ne 0) {
    throw 'SBOM generation failed.'
}

# The public half of the signing certificate, so a user can check and trust it (docs/install.md).
Export-Certificate -Cert $certificate -FilePath (Join-Path $OutDir 'BitwardenCommandPalette-signing.cer') | Out-Null

$sums = Get-ChildItem $OutDir -File | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
Set-Content (Join-Path $OutDir 'SHA256SUMS.txt') $sums -Encoding ascii

Get-ChildItem $OutDir | Format-Table Name, Length -AutoSize
