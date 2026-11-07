#Requires -Version 5.1
<#
.SYNOPSIS
Records the Bitwarden CLI's output from a throwaway test vault, scrubbed, for
the fake bw in tests/FakeBw.

.DESCRIPTION
Only ever run this against a test account made for this purpose. The script:

- uses a fresh, temporary bw data folder, so your normal bw login is untouched;
- asks you to log in to the test account with bw's own prompt;
- refuses to record if the vault holds any item or folder it did not create
  (names starting with "BWCP Fixture"), before anything is written;
- deletes and recreates its fixture items, then records each command;
- replaces the account email, IDs, dates, session keys, your user folder and a
  self-hosted server URL before writing, and checks none of them is left;
- logs out and deletes the temporary data folder at the end.

The master password goes to bw through --passwordenv on the child's
environment only, as the extension does. Review the files with git diff before
they are committed.

.PARAMETER BwPath
Path to bw.exe. Defaults to the bw found on PATH.

.PARAMETER ServerUrl
Server of the test account, when it is not bitwarden.com.

.PARAMETER MasterPassword
Master password of the test account as a SecureString. Prompted for when not
given.

.PARAMETER OutputDirectory
Where the recordings go. Existing .json files there are replaced.
#>
[CmdletBinding()]
param(
    [string] $BwPath,
    [string] $ServerUrl,
    [securestring] $MasterPassword,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\tests\FakeBw\recordings')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$FixturePrefix = 'BWCP Fixture'
$PasswordVariable = 'BWCP_MASTER_PASSWORD'
# The RFC 6238 example secret; not a real account's.
$FakeTotpSecret = 'JBSWY3DPEHPK3PXP'
$WrongPassword = 'not-the-master-password'
$InvalidSession = [Convert]::ToBase64String([byte[]]::new(64))
$FixedDate = '2026-01-01T00:00:00.000Z'
# Kept as character codes: Windows PowerShell reads a script without a BOM as ANSI.
$Unicode = "$([char]0xDC)n$([char]0xEF)code $([char]0x2713) $([char]0x540D)$([char]0x524D)"
$FixtureNames = [ordered]@{
    'full-login'  = "$FixturePrefix - Full login"
    'no-totp'     = "$FixturePrefix - No TOTP"
    'no-username' = "$FixturePrefix - No username"
    'no-password' = "$FixturePrefix - No password"
    'totp-uri'    = "$FixturePrefix - TOTP URI"
    'reprompt'    = "$FixturePrefix - Reprompt"
    'duplicate'   = "$FixturePrefix - Duplicate name"
    'unicode'     = "$FixturePrefix - $Unicode"
    'card'        = "$FixturePrefix - Card"
    'secure-note' = "$FixturePrefix - Secure note"
}

$script:Recordings = New-Object System.Collections.Generic.List[object]
$script:SessionKeys = New-Object System.Collections.Generic.List[string]

function ConvertTo-WindowsArgument([string] $Value) {
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $builder = New-Object System.Text.StringBuilder '"'
    $backslashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') { $backslashes++; continue }
        if ($character -eq '"') { [void]$builder.Append('\', 2 * $backslashes + 1) }
        elseif ($backslashes -gt 0) { [void]$builder.Append('\', $backslashes) }
        $backslashes = 0
        [void]$builder.Append($character)
    }
    [void]$builder.Append('\', 2 * $backslashes).Append('"')
    $builder.ToString()
}

# Runs bw with the given arguments. Secrets go only into the child's
# environment; BW_SESSION and the password variable inherited from this shell
# are removed so every call states exactly what it gets.
function Invoke-Bw {
    param([string[]] $Arguments, [hashtable] $Environment = @{})

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $script:Bw
    $startInfo.Arguments = ($Arguments | ForEach-Object { ConvertTo-WindowsArgument $_ }) -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    foreach ($name in 'BW_SESSION', $PasswordVariable) { [void]$startInfo.Environment.Remove($name) }
    foreach ($name in $Environment.Keys) { $startInfo.Environment[$name] = $Environment[$name] }

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) {
            $process.Kill()
            throw "bw $($Arguments[0]) did not finish within two minutes."
        }
        $process.WaitForExit()
        [pscustomobject]@{
            Arguments  = $Arguments
            ExitCode   = $process.ExitCode
            Stdout     = $stdout.Result
            Stderr     = $stderr.Result
            DurationMs = $stopwatch.ElapsedMilliseconds
        }
    }
    finally {
        $process.Dispose()
    }
}

# Runs bw and keeps the result for writing. $Labels names what each environment
# variable held, since the values themselves are never written.
function Save-Bw {
    param([string] $Name, [string[]] $Arguments, [hashtable] $Environment = @{}, [hashtable] $Labels = @{})
    $result = Invoke-Bw $Arguments $Environment
    $script:Recordings.Add([pscustomobject]@{ Name = $Name; Labels = $Labels; Result = $result })
    $result
}

function Assert-Success($Result, [string] $What) {
    # The output is not shown: it may hold vault data.
    if ($Result.ExitCode -ne 0) { throw "$What failed with exit code $($Result.ExitCode)." }
}

# Parses bw's JSON without ever echoing it: ConvertFrom-Json's own error
# message would quote the text, which may hold vault data.
function ConvertFrom-BwJson($Result, [string] $What) {
    Assert-Success $Result $What
    try { $Result.Stdout | ConvertFrom-Json }
    catch { throw "$What returned output that is not JSON." }
}

# Windows PowerShell returns a JSON array as one object; unroll it.
function Get-BwList([string] $Kind, [hashtable] $Environment) {
    $parsed = ConvertFrom-BwJson (Invoke-Bw @('list', $Kind, '--nointeraction') $Environment) "Listing $Kind"
    , @($parsed | ForEach-Object { $_ })
}

function ConvertTo-Base64Json($Value) {
    $json = $Value | ConvertTo-Json -Depth 10 -Compress
    [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($json))
}

# Untyped on purpose: a [string] parameter turns $null into '', and the fixtures
# must send null for a missing username, password, TOTP or folder.
function New-Login($Name, $Username, $Password, $Totp, [string[]] $Uris, [int] $Reprompt = 0, $FolderId = $null) {
    @{
        organizationId = $null; collectionIds = $null; folderId = $FolderId
        type = 1; name = $Name; notes = $null; favorite = $false; fields = @(); reprompt = $Reprompt
        login = @{
            username = $Username; password = $Password; totp = $Totp
            uris = @($Uris | Where-Object { $_ } | ForEach-Object { @{ match = $null; uri = $_ } })
        }
        secureNote = $null; card = $null; identity = $null
    }
}

function Get-Fixture($FolderId) {
    $n = $FixtureNames
    $full = New-Login $n['full-login'] 'fixture-user@example.com' 'fixture-password-full' $FakeTotpSecret @('https://login.example.com', 'https://example.org/sign-in') -FolderId $FolderId
    $full.notes = 'Fixture notes.'
    $full.fields = @(@{ name = 'Fixture field'; value = 'fixture-field-value'; type = 0 })
    @(
        $full
        New-Login $n['no-totp'] 'fixture-no-totp' 'fixture-password-no-totp' $null @('https://no-totp.example.com')
        New-Login $n['no-username'] $null 'fixture-password-no-username' $null @()
        New-Login $n['no-password'] 'fixture-no-password' $null $null @()
        New-Login $n['totp-uri'] 'fixture-totp-uri' 'fixture-password-totp-uri' "otpauth://totp/Fixture?secret=$FakeTotpSecret&issuer=BWCP" @()
        New-Login $n['reprompt'] 'fixture-reprompt' 'fixture-password-reprompt' $null @() -Reprompt 1
        New-Login $n['duplicate'] 'fixture-duplicate-1' 'fixture-password-duplicate-1' $null @()
        New-Login $n['duplicate'] 'fixture-duplicate-2' 'fixture-password-duplicate-2' $null @()
        New-Login $n['unicode'] "fixture-$Unicode" "fixture-password-$Unicode" $null @()
        @{
            organizationId = $null; collectionIds = $null; folderId = $null
            type = 3; name = $n['card']; notes = $null; favorite = $false; fields = @(); reprompt = 0
            login = $null; secureNote = $null; identity = $null
            # A published test card number.
            card = @{ cardholderName = 'Fixture Holder'; brand = 'Visa'; number = '4111111111111111'; expMonth = '1'; expYear = '2030'; code = '123' }
        }
        @{
            organizationId = $null; collectionIds = $null; folderId = $null
            type = 2; name = $n['secure-note']; notes = 'Fixture secure note.'; favorite = $false; fields = @(); reprompt = 0
            login = $null; secureNote = @{ type = 0 }; card = $null; identity = $null
        }
    )
}

function Protect-Text([string] $Text, [hashtable] $Literals, [hashtable] $GuidMap) {
    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    foreach ($literal in ($Literals.Keys | Sort-Object Length -Descending)) {
        $Text = [regex]::Replace($Text, [regex]::Escape($literal), $Literals[$literal].Replace('$', '$$'), 'IgnoreCase')
    }
    $Text = [regex]::Replace($Text, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}', {
            param($match)
            $key = $match.Value.ToLowerInvariant()
            if (-not $GuidMap.ContainsKey($key)) { $GuidMap[$key] = '00000000-0000-4000-8000-{0:d12}' -f ($GuidMap.Count + 1) }
            $GuidMap[$key]
        })
    [regex]::Replace($Text, '\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z', $FixedDate)
}

function Get-Format([string] $Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return 'empty' }
    if ($Text.TrimStart()[0] -notin '{', '[') { return 'text' }
    try { [void]($Text | ConvertFrom-Json); 'json' } catch { 'text' }
}

# A path as bw might print it: long and 8.3 short form, each with plain,
# JSON-escaped and forward slashes.
function Get-PathForm([string] $Path) {
    $forms = @($Path)
    try { $forms += (New-Object -ComObject Scripting.FileSystemObject).GetFolder($Path).ShortPath } catch { Write-Verbose 'No short path form.' }
    $forms | Select-Object -Unique | ForEach-Object { $_; $_.Replace('\', '\\'); $_.Replace('\', '/') }
}

function Write-Utf8File([string] $Path, [string] $Content) {
    [System.IO.File]::WriteAllText($Path, $Content.Replace("`r`n", "`n").TrimEnd("`n") + "`n", [System.Text.UTF8Encoding]::new($false))
}

$script:Bw = if ($BwPath) { (Resolve-Path $BwPath).Path } else { (Get-Command bw -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source }
$signature = Get-AuthenticodeSignature $script:Bw
# Under LOCALAPPDATA rather than TEMP, which can be an 8.3 short path.
$dataDirectory = Join-Path $env:LOCALAPPDATA ('Temp\bwcp-recording-' + [guid]::NewGuid().ToString('n'))
$previousDataDirectory = $env:BITWARDENCLI_APPDATA_DIR
$plainPassword = $null
$loggedIn = $false

[void](New-Item -ItemType Directory -Path $dataDirectory)
$env:BITWARDENCLI_APPDATA_DIR = $dataDirectory
try {
    $signer = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
    Write-Host "bw: $script:Bw ($($signature.Status), $signer)"
    $version = (Save-Bw 'version' @('--version')).Stdout.Trim()
    [void](Save-Bw 'status-unauthenticated' @('status'))
    [void](Save-Bw 'list-items-unauthenticated' @('list', 'items', '--nointeraction'))

    if ($ServerUrl) { Assert-Success (Invoke-Bw @('config', 'server', $ServerUrl)) 'Setting the server' }

    Write-Host ''
    Write-Host 'Log in to the THROWAWAY TEST ACCOUNT with bw''s own prompt. Never use your real account.'
    & $script:Bw login
    if ($LASTEXITCODE -ne 0) { throw 'bw login failed.' }
    $loggedIn = $true
    # Login printed a session key; locking makes it useless.
    Assert-Success (Invoke-Bw @('lock')) 'Locking after login'
    Clear-Host

    $status = ConvertFrom-BwJson (Invoke-Bw @('status')) 'Reading the status'
    Write-Host "Logged in as $($status.userEmail) on $($status.serverUrl)."
    $confirmation = Read-Host 'Type that email again to confirm this is a throwaway test account'
    if ($confirmation -ne $status.userEmail) { throw 'Not confirmed; nothing was recorded.' }

    $secure = if ($MasterPassword) { $MasterPassword } else { Read-Host 'Master password of the test account' -AsSecureString }
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }

    $unlock = @('unlock', '--passwordenv', $PasswordVariable, '--raw', '--nointeraction')
    $rightPassword = @{ $PasswordVariable = $plainPassword }
    $rightLabel = @{ $PasswordVariable = '[master-password]' }
    $wrongPassword = @{ $PasswordVariable = $WrongPassword }
    $wrongLabel = @{ $PasswordVariable = $WrongPassword }

    [void](Save-Bw 'status-locked' @('status'))
    [void](Save-Bw 'list-items-locked' @('list', 'items', '--nointeraction'))
    [void](Save-Bw 'unlock-missing-password-variable' $unlock)
    [void](Save-Bw 'unlock-wrong-password' $unlock $wrongPassword $wrongLabel)
    [void](Save-Bw 'unlock-wrong-password-response' ($unlock + '--response') $wrongPassword $wrongLabel)
    $unlocked = Save-Bw 'unlock' $unlock $rightPassword $rightLabel
    Assert-Success $unlocked 'Unlocking (check the master password)'
    $session = $unlocked.Stdout.Trim()
    $script:SessionKeys.Add($session)
    $withSession = @{ BW_SESSION = $session }
    $sessionLabel = @{ BW_SESSION = '[session]' }

    # Guard: nothing but our own fixtures may be in this vault. Checked in
    # memory before any vault content is recorded.
    Assert-Success (Invoke-Bw @('sync', '--nointeraction') $withSession) 'Syncing'
    # Get-BwList's result is assigned before filtering: piped directly, the
    # whole array would pass through Where-Object as one object.
    $existingItems = Get-BwList 'items' $withSession
    $allFolders = Get-BwList 'folders' $withSession
    $existingFolders = @($allFolders | Where-Object { $_.id })
    $foreignItems = @($existingItems | Where-Object { $_.PSObject.Properties['organizationId'] -and $_.organizationId -or -not $_.name.StartsWith($FixturePrefix, [StringComparison]::Ordinal) })
    $foreignFolders = @($existingFolders | Where-Object { -not $_.name.StartsWith($FixturePrefix, [StringComparison]::Ordinal) })
    $foreign = $foreignItems.Count + $foreignFolders.Count
    if ($foreign -gt 0) {
        throw "This vault holds $foreign item(s) or folder(s) this script did not create, or organisation items. Use an empty throwaway test account. Nothing was recorded."
    }

    Write-Host 'Recreating the fixture items...'
    foreach ($item in $existingItems) {
        Assert-Success (Invoke-Bw @('delete', 'item', $item.id, '--permanent', '--nointeraction') $withSession) 'Deleting an old fixture'
    }
    foreach ($folder in $existingFolders) {
        Assert-Success (Invoke-Bw @('delete', 'folder', $folder.id, '--nointeraction') $withSession) 'Deleting an old fixture folder'
    }
    $folder = Invoke-Bw @('create', 'folder', (ConvertTo-Base64Json @{ name = "$FixturePrefix folder" }), '--nointeraction') $withSession
    $folderId = (ConvertFrom-BwJson $folder 'Creating the fixture folder').id
    foreach ($item in Get-Fixture $folderId) {
        Assert-Success (Invoke-Bw @('create', 'item', (ConvertTo-Base64Json $item), '--nointeraction') $withSession) 'Creating a fixture'
    }

    Write-Host 'Recording...'
    $s = @{ Environment = $withSession; Labels = $sessionLabel }
    [void](Save-Bw 'sync' @('sync', '--nointeraction') @s)
    [void](Save-Bw 'status-unlocked' @('status') @s)
    [void](Save-Bw 'get-template-item' @('get', 'template', 'item') @s)
    $list = Save-Bw 'list-items' @('list', 'items', '--nointeraction') @s
    [void](Save-Bw 'list-items-response' @('list', 'items', '--response', '--nointeraction') @s)
    [void](Save-Bw 'list-items-search' @('list', 'items', '--search', "$FixturePrefix - Full", '--nointeraction') @s)
    [void](Save-Bw 'list-folders' @('list', 'folders', '--nointeraction') @s)
    [void](Save-Bw 'list-items-invalid-session' @('list', 'items', '--nointeraction') @{ BW_SESSION = $InvalidSession } @{ BW_SESSION = '[invalid-session]' })

    $listed = @(ConvertFrom-BwJson $list 'Listing items' | ForEach-Object { $_ })
    $ids = @{}
    foreach ($key in $FixtureNames.Keys) {
        $ids[$key] = @($listed | Where-Object name -CEQ $FixtureNames[$key])[0].id
    }
    $logins = 'full-login', 'no-totp', 'no-username', 'no-password', 'totp-uri', 'reprompt', 'unicode'
    foreach ($key in $logins) {
        foreach ($field in 'username', 'password', 'totp') {
            [void](Save-Bw "get-$field-$key" @('get', $field, $ids[$key], '--nointeraction') @s)
        }
    }
    foreach ($key in 'full-login', 'reprompt', 'card', 'secure-note') {
        [void](Save-Bw "get-item-$key" @('get', 'item', $ids[$key], '--nointeraction') @s)
    }
    foreach ($key in 'card', 'secure-note') {
        foreach ($field in 'password', 'totp') {
            [void](Save-Bw "get-$field-$key" @('get', $field, $ids[$key], '--nointeraction') @s)
        }
    }
    [void](Save-Bw 'get-password-response' @('get', 'password', $ids['full-login'], '--response', '--nointeraction') @s)
    [void](Save-Bw 'get-totp-no-totp-response' @('get', 'totp', $ids['no-totp'], '--response', '--nointeraction') @s)
    [void](Save-Bw 'get-password-unknown-id' @('get', 'password', [guid]::NewGuid().ToString(), '--nointeraction') @s)
    [void](Save-Bw 'get-password-by-duplicate-name' @('get', 'password', $FixtureNames['duplicate'], '--nointeraction') @s)

    [void](Save-Bw 'lock' @('lock') @s)
    $old = @{ Environment = $withSession; Labels = @{ BW_SESSION = '[session-before-lock]' } }
    [void](Save-Bw 'status-after-lock' @('status') @old)
    [void](Save-Bw 'get-password-after-lock' @('get', 'password', $ids['full-login'], '--nointeraction') @old)

    # Scrub. Literal values first, then IDs and dates.
    # $forbidden is what must not survive scrubbing, checked on the raw strings
    # before they are serialized (ConvertTo-Json escapes backslashes).
    $literals = @{ $status.userEmail = 'user@example.com' }
    $forbidden = New-Object System.Collections.Generic.List[string]
    $forbidden.Add($status.userEmail)
    foreach ($form in Get-PathForm $dataDirectory) { $literals[$form] = '[bw-data]'; $forbidden.Add($form) }
    foreach ($form in Get-PathForm $env:USERPROFILE) {
        $literals[$form] = if ($form.Contains('/')) { 'C:/Users/user' } elseif ($form.Contains('\\')) { 'C:\\Users\\user' } else { 'C:\Users\user' }
        $forbidden.Add($form)
    }
    # A self-hosted server is replaced by host name, which covers every way bw
    # might print the URL (trailing slash, path, escaped slashes).
    $standardHosts = 'bitwarden.com', 'vault.bitwarden.com', 'bitwarden.eu', 'vault.bitwarden.eu'
    foreach ($url in @($status.serverUrl, $ServerUrl) | Where-Object { $_ }) {
        $serverHost = ([uri]$url).Host
        if ($standardHosts -notcontains $serverHost) { $literals[$serverHost] = 'bitwarden.example.com'; $forbidden.Add($serverHost) }
    }
    for ($i = 0; $i -lt $script:SessionKeys.Count; $i++) {
        $fake = [Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes((('fake-session-key-{0}-' -f ($i + 1)) * 4).PadRight(64, '-').Substring(0, 64)))
        $literals[$script:SessionKeys[$i]] = $fake
    }
    $masterPasswordSeenIn = @($script:Recordings | Where-Object { "$($_.Result.Stdout)$($_.Result.Stderr)".Contains($plainPassword) } | ForEach-Object Name)
    $literals[$plainPassword] = '[master-password]'
    $forbidden.AddRange($script:SessionKeys)
    $forbidden.Add($plainPassword)

    $guidMap = @{}
    $scrubbed = foreach ($recording in $script:Recordings) {
        $result = $recording.Result
        [pscustomobject]@{
            Name      = $recording.Name
            Arguments = @($result.Arguments | ForEach-Object { Protect-Text $_ $literals $guidMap })
            Labels    = $recording.Labels
            Result    = $result
            Stdout    = Protect-Text $result.Stdout $literals $guidMap
            Stderr    = Protect-Text $result.Stderr $literals $guidMap
        }
    }
    $forbidden.AddRange([string[]]@($guidMap.Keys))
    foreach ($recording in $scrubbed) {
        $text = (@($recording.Arguments) + $recording.Stdout + $recording.Stderr) -join "`n"
        foreach ($value in $forbidden) {
            if ($value -and $text.IndexOf($value, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Scrubbing missed a value in $($recording.Name). Nothing was written."
            }
        }
    }
    $files = foreach ($recording in $scrubbed) {
        $content = [ordered]@{
            arguments    = $recording.Arguments
            environment  = $recording.Labels
            exitCode     = $recording.Result.ExitCode
            durationMs   = $recording.Result.DurationMs
            stdoutFormat = Get-Format $recording.Result.Stdout
            stdout       = $recording.Stdout
            stderr       = $recording.Stderr
        }
        [pscustomobject]@{ Name = $recording.Name; Json = $content | ConvertTo-Json -Depth 5 }
    }

    [void](New-Item -ItemType Directory -Force -Path $OutputDirectory)
    Get-ChildItem -Path $OutputDirectory -Filter '*.json' | Remove-Item
    foreach ($file in $files) { Write-Utf8File (Join-Path $OutputDirectory "$($file.Name).json") $file.Json }
    $manifest = [ordered]@{
        bwVersion            = $version
        bwFile               = Split-Path $script:Bw -Leaf
        signature            = "$($signature.Status)"
        signer               = $signer
        recordedOn           = (Get-Date).ToString('yyyy-MM-dd')
        masterPasswordSeenIn = $masterPasswordSeenIn
        recordings           = @($files | ForEach-Object Name)
    }
    Write-Utf8File (Join-Path $OutputDirectory 'manifest.json') ($manifest | ConvertTo-Json -Depth 3)
    Write-Host "Wrote $($files.Count) recordings to $((Resolve-Path $OutputDirectory).Path). Review them with git diff before committing."
}
finally {
    $plainPassword = $null
    if ($loggedIn) {
        try { [void](Invoke-Bw @('logout', '--nointeraction')) }
        catch { Write-Warning 'bw logout failed; the temporary data folder is deleted anyway.' }
    }
    $env:BITWARDENCLI_APPDATA_DIR = $previousDataDirectory
    Remove-Item -Recurse -Force -Path $dataDirectory -ErrorAction SilentlyContinue
    if (Test-Path $dataDirectory) {
        Write-Warning "Could not delete $dataDirectory, which holds the test account's encrypted bw data. Delete it yourself."
    }
}
