$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) "win-mix-release-check-$([Guid]::NewGuid())"
New-Item $scratch -ItemType Directory | Out-Null
try {
    Copy-Item (Join-Path $PSScriptRoot '../build.ps1') $scratch
    foreach ($version in '1.0.1', '01.0.1', '1.0', '1.0.65536', '1.0.1-preview', '1.0.1;bad') {
        Set-Content (Join-Path $scratch 'VERSION') $version
        $expected = if ($version -eq '1.0.1') { 'Tag must be v1.0.1.' } else { 'Version must contain three integers from 0 to 65535 without leading zeros.' }
        if ($version -eq '1.0.1') {
            Set-Content (Join-Path $scratch 'win-mix-Setup-1.0.1-x64.exe.sha256') 'stale'
            Set-Content (Join-Path $scratch 'win-mix-Setup-1.0.1-x64.exe.defender.json') 'stale'
            Set-Content (Join-Path $scratch 'win-mix-Setup-1.0.1-x64.exe.application-scan.log') 'stale'
            Set-Content (Join-Path $scratch 'win-mix-Setup-1.0.1-x64.exe.installer-scan.log') 'stale'
        }
        $message = ''
        try { & (Join-Path $scratch 'build.ps1') -ExpectedTag 'v9.9.9' -Dotnet 'must-not-run' -OutputDirectory $scratch }
        catch { $message = $_.Exception.Message }
        if ($message -cne $expected) { throw "Release guard failed for '$version': $message" }
        if ((Test-Path (Join-Path $scratch 'win-mix-Setup-1.0.1-x64.exe.sha256')) -or
            (Test-Path (Join-Path $scratch 'win-mix-Setup-1.0.1-x64.exe.defender.json'))) { throw 'Tag failure left stale success markers.' }
        if (Get-ChildItem $scratch -Filter '*-scan.log') { throw 'Preflight failure left stale scan logs.' }
    }
    Write-Output 'Release version/tag guards passed before build execution.'

    Set-Content (Join-Path $scratch 'VERSION') '1.0.1'
    New-Item (Join-Path $scratch 'Tests'), (Join-Path $scratch 'Licenses') -ItemType Directory | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'PublishChecks.ps1') (Join-Path $scratch 'Tests/PublishChecks.ps1')
    Set-Content (Join-Path $scratch 'LICENSE') 'fixture license'
    Set-Content (Join-Path $scratch 'Licenses/notice.txt') 'fixture notice'
    Set-Content (Join-Path $scratch 'fixture.dll') 'harmless package fixture'
    function Get-Item {
        param([string]$Path, [string]$LiteralPath, [switch]$Force)
        $actual = if ($LiteralPath) { $LiteralPath } else { $Path }
        $item = Microsoft.PowerShell.Management\Get-Item -LiteralPath $actual -Force:$Force
        if ($item.Name -in 'win-mix.dll', 'win-mix-Setup-1.0.1-x64.exe') {
            return [pscustomobject]@{ VersionInfo = [pscustomobject]@{ ProductVersion = '1.0.1'; FileVersion = '1.0.1.0' } }
        }
        $item
    }
    function Move-Item {
        param([string]$LiteralPath, [string]$Destination)
        if ($global:failScan -eq 'evidence') { throw 'Evidence publication failed.' }
        Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination
    }
    Set-Content (Join-Path $scratch 'dotnet.ps1') @'
if ($args[0] -eq 'publish') {
    New-Item 'publish/Assets' -ItemType Directory -Force | Out-Null
    Copy-Item 'fixture.dll' 'publish/win-mix.dll'
    Copy-Item 'LICENSE' 'publish/LICENSE'
    Copy-Item 'Licenses' 'publish' -Recurse
    foreach ($file in 'win-mix.pri', 'App.xbf', 'Assets/app.ico', 'Assets/app-icon.png') {
        Set-Content (Join-Path 'publish' $file) 'fixture'
    }
}
exit 0
'@
    Set-Content (Join-Path $scratch 'inno.ps1') @'
$global:releaseStages.Add('package')
Copy-Item 'fixture.dll' 'win-mix-Setup-1.0.1-x64.exe'
exit 0
'@
    Set-Content (Join-Path $scratch 'scan-release.ps1') @'
param([string]$Path)
$stage = if (Test-Path -LiteralPath $Path -PathType Container) { 'application' } else { 'installer' }
$expected = if ($stage -eq 'application') { Join-Path $PSScriptRoot 'publish' } else { Join-Path $PSScriptRoot 'win-mix-Setup-1.0.1-x64.exe' }
if ($Path -cne $expected) { throw 'Unexpected scan target.' }
$global:releaseStages.Add($stage)
if ($global:failScan -eq $stage) { throw 'Defender scan failed or detected a threat.' }
$file = if ($stage -eq 'application') { Join-Path $Path 'win-mix.dll' } else { $Path }
[pscustomobject]@{ Files = @([pscustomobject]@{ Path = [IO.Path]::GetFileName($file); SHA256 = (Get-FileHash $file).Hash }) }
'@
    foreach ($failure in 'application', 'installer', 'evidence', '') {
        $global:releaseStages = [Collections.Generic.List[string]]::new()
        $global:failScan = $failure
        $installer = Join-Path $scratch 'win-mix-Setup-1.0.1-x64.exe'
        # Failed retries must remove old success markers too.
        Set-Content "$installer.sha256" 'stale'
        Set-Content "$installer.defender.json" 'stale'
        $message = ''
        try {
            & (Join-Path $scratch 'build.ps1') -Dotnet (Join-Path $scratch 'dotnet.ps1') -InnoCompiler (Join-Path $scratch 'inno.ps1') -OutputDirectory $scratch
        } catch { $message = $_.Exception.Message }
        if ($failure) {
            $expectedError = if ($failure -eq 'evidence') { 'Evidence publication failed.' } else { 'Defender scan failed or detected a threat.' }
            if ($message -ne $expectedError -or
                (Test-Path "$installer.sha256") -or (Test-Path "$installer.defender.json") -or (Test-Path "$installer.defender.json.tmp")) { throw "Failure did not block release: $message" }
            $expected = if ($failure -eq 'application') { 'application' } else { 'application,package,installer' }
        } else {
            if ($message) { throw $message }
            $expected = 'application,package,installer'
            $evidence = Get-Content "$installer.defender.json" -Raw | ConvertFrom-Json
            $hash = (Get-FileHash $installer).Hash
            if ($evidence.Installer.Files[0].SHA256 -ne $hash -or
                $evidence.Application.Files[0].SHA256 -ne (Get-FileHash (Join-Path $scratch 'publish/win-mix.dll')).Hash -or
                (Get-Content "$installer.sha256") -cne "$($hash.ToLowerInvariant())  win-mix-Setup-1.0.1-x64.exe") { throw 'Release evidence does not match scanned artifacts.' }
        }
        if (($global:releaseStages -join ',') -cne $expected) { throw 'Incorrect scan/package ordering.' }
    }
    Write-Output 'Release scan ordering, failure gates, stale evidence and successful sidecars passed.'
} finally {
    # This unique directory was created above; validate its absolute parent before cleanup.
    $item = Get-Item -LiteralPath $scratch
    if ($item.Parent.FullName -ne [IO.Path]::GetTempPath().TrimEnd('\', '/') -or
        $item.Name -notlike 'win-mix-release-check-*' -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $item.FullName -Recurse -Force
    Remove-Variable releaseStages, failScan -Scope Global -ErrorAction SilentlyContinue
}
