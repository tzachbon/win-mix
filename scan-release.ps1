param(
    [Parameter(Mandatory)][string]$Path,
    [string]$Scanner
)
$ErrorActionPreference = 'Stop'
$status = Get-MpComputerStatus
if ($status.AntivirusEnabled -ne $true -or $status.AMRunningMode -ne 'Normal' -or
    !$status.AMProductVersion -or !$status.AMEngineVersion -or !$status.AntivirusSignatureVersion) {
    throw 'Defender must be running in normal mode with available version information.'
}
if ($status.DefenderSignaturesOutOfDate -ne $false -or !$status.AntivirusSignatureLastUpdated -or
    $status.AntivirusSignatureLastUpdated -lt (Get-Date).AddHours(-48) -or
    $status.AntivirusSignatureLastUpdated -gt (Get-Date)) {
    throw 'Defender signatures must be current and updated within the last 48 hours.'
}
if (!$Scanner) {
    $Scanner = Join-Path $env:ProgramData "Microsoft/Windows Defender/Platform/$($status.AMProductVersion)-0/MpCmdRun.exe"
    if (!(Test-Path -LiteralPath $Scanner -PathType Leaf)) {
        $Scanner = Join-Path $env:ProgramFiles 'Windows Defender/MpCmdRun.exe'
    }
}
if (!(Test-Path -LiteralPath $Scanner -PathType Leaf)) { throw 'Defender scanner is unavailable.' }
$target = Get-Item -LiteralPath $Path
function Get-Manifest {
    $files = if ($target.PSIsContainer) { @(Get-ChildItem -LiteralPath $target.FullName -File -Recurse -Force) } else { @($target) }
    if (!$files.Count) { throw 'Cannot scan an empty release artifact.' }
    @($files | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{
            Path = if ($target.PSIsContainer) { $_.FullName.Substring($target.FullName.Length + 1).Replace('\', '/') } else { $_.Name }
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
}
$before = @(Get-Manifest)
$started = [datetime]::UtcNow
$global:LASTEXITCODE = $null
& $Scanner -Scan -ScanType 3 -File $target.FullName -DisableRemediation | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Defender scan failed or detected a threat (exit $LASTEXITCODE). No release is permitted." }
$after = @(Get-Manifest)
if ((ConvertTo-Json -InputObject $before -Compress) -cne (ConvertTo-Json -InputObject $after -Compress)) {
    throw 'Release files changed during the scan. No release is permitted.'
}
[pscustomobject]@{
    StartedUtc = $started.ToString('o')
    FinishedUtc = [datetime]::UtcNow.ToString('o')
    ProductVersion = $status.AMProductVersion
    EngineVersion = $status.AMEngineVersion
    SignatureVersion = $status.AntivirusSignatureVersion
    SignatureUpdatedUtc = $status.AntivirusSignatureLastUpdated.ToUniversalTime().ToString('o')
    ScannerVersion = (Get-Item -LiteralPath $Scanner).VersionInfo.FileVersion
    Files = $before
}
