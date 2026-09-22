$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) "win-mix-defender-check-$([Guid]::NewGuid())"
New-Item $scratch -ItemType Directory | Out-Null
$scanScript = Join-Path $PSScriptRoot '../scan-release.ps1'
$fixture = Join-Path $scratch 'app.dll'
$scanner = Join-Path $scratch 'scanner.ps1'
$log = Join-Path $scratch 'scan.log'
function Get-MpComputerStatus { $global:DefenderTestStatus }
function Assert-Failure([scriptblock]$Action, [string]$Message) {
    $caught = ''
    try { & $Action | Out-Null } catch { $caught = $_.Exception.Message }
    if ($caught -notlike $Message) { throw "Expected '$Message', got '$caught'." }
}
try {
    $global:DefenderTestStatus = [pscustomobject]@{
        AntivirusEnabled = $true; AMRunningMode = 'Normal'
        DefenderSignaturesOutOfDate = $false
        AntivirusSignatureLastUpdated = Get-Date
        AntivirusSignatureVersion = 'test-signature'; AMEngineVersion = 'test-engine'
        AMProductVersion = 'test-platform'
    }
    Set-Content $fixture 'harmless test fixture'
    Set-Content $scanner @'
if (($args -join '|') -ne "-Scan|-ScanType|3|-File|$global:expectedScanPath|-DisableRemediation") {
    throw 'Incorrect scan arguments.'
}
Write-Output 'Fixture scan completed.'
exit 0
'@
    $global:expectedScanPath = $fixture
    Set-Content $log 'stale scan output'
    $result = & $scanScript -Path $fixture -Scanner $scanner -LogPath $log
    if ((Get-Content $log -Raw).Trim() -cne 'Fixture scan completed.') { throw 'Scan output was not captured or stale output survived.' }
    if ($result.Files.Count -ne 1 -or $result.Files[0].SHA256 -ne (Get-FileHash $fixture).Hash) {
        throw 'A successful scan must identify the exact scanned bytes.'
    }
    foreach ($code in 2, 5) {
        Set-Content $scanner "exit $code"
        Assert-Failure { & $scanScript -Path $fixture -Scanner $scanner } '*scan failed or detected a threat*'
    }
    Set-Content $scanner 'throw "Scanner must not run."'
    $global:DefenderTestStatus.AntivirusEnabled = $false
    Assert-Failure { & $scanScript -Path $fixture -Scanner $scanner } '*Defender must be running*'
    $global:DefenderTestStatus.AntivirusEnabled = $true
    $global:DefenderTestStatus.AntivirusSignatureLastUpdated = (Get-Date).AddDays(-3)
    Set-Content $log 'stale successful scan'
    Assert-Failure { & $scanScript -Path $fixture -Scanner $scanner -LogPath $log } '*signatures must be*'
    if ((Get-Item $log).Length -ne 0) { throw 'Preflight failure retained stale scan output.' }
    $global:DefenderTestStatus.AntivirusSignatureLastUpdated = Get-Date
    Assert-Failure { & $scanScript -Path $fixture -Scanner (Join-Path $scratch 'missing.exe') } '*scanner is unavailable*'
    Set-Content $scanner 'Set-Content $global:expectedScanPath "changed during scan"; exit 0'
    Assert-Failure { & $scanScript -Path $fixture -Scanner $scanner } '*changed during the scan*'
    Write-Output 'Defender scan checks passed: exact arguments/hashes, detection/error, unavailable/stale protection and changed bytes.'
} finally {
    # Only these named fixture files are created in this unique temporary directory.
    foreach ($file in $fixture, $scanner, $log) { Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $scratch
    Remove-Variable expectedScanPath, DefenderTestStatus -Scope Global -ErrorAction SilentlyContinue
}
