$ErrorActionPreference = 'Stop'
$diagnostic = Join-Path $PSScriptRoot '../diagnose-defender.ps1'
$scratch = Join-Path ([IO.Path]::GetTempPath()) "win-mix-diagnostic-$([Guid]::NewGuid())"
New-Item -ItemType Directory -Path $scratch | Out-Null
function Get-MpThreatDetection {
    [CmdletBinding()]param()
    if ($global:DiagnosticTestFailHistory) { throw 'private underlying error' }
    $global:DiagnosticTestHistory
}
try {
    $global:DiagnosticTestFailHistory = $false
    $global:DiagnosticTestHistory = @([pscustomobject]@{
        InitialDetectionTime = [datetime]'2026-01-01'; ThreatID = 123; ActionSuccess = $false
        Resources = @("file:_$scratch\win-mix.dll", "file:_$scratch\nested\other.dll",
            "file:_$scratch-other\win-mix.dll", "file:_$scratch\..\unrelated.dll", 'process:_unrelated')
    }, [pscustomobject]@{ Resources = @("file:_$scratch-other\win-mix.dll") })
    $report = & $diagnostic -InstallDirectory ($scratch.ToUpperInvariant() + '\.')
    if ($report.Detections.Count -ne 1 -or
        ($report.Detections[0].Files -join ',') -ne 'win-mix.dll,nested\other.dll' -or
        $report.Detections[0].ThreatID -ne 123 -or $report.Detections[0].ActionSuccess -ne $false -or
        ($report.MissingFiles -join ',') -ne 'win-mix.exe,win-mix.dll') { throw 'Detection filtering or missing-file reporting failed.' }
    if (($report | ConvertTo-Json -Depth 4) -match [regex]::Escape($scratch)) { throw 'Report exposed an absolute path.' }
    $global:DiagnosticTestHistory = @()
    Set-Content -LiteralPath (Join-Path $scratch 'win-mix.exe') -Value 'harmless fixture'
    $report = & $diagnostic -InstallDirectory $scratch
    if ($report.Detections.Count -ne 0 -or $report.History -ne 'No recorded matching detections.' -or
        $report.Note -notlike '*not a current safety verdict*' -or
        ($report.MissingFiles -join ',') -ne 'win-mix.dll') { throw 'Empty history or missing DLL reporting failed.' }
    Set-Content -LiteralPath (Join-Path $scratch 'win-mix.dll') -Value 'harmless fixture'
    if ((& $diagnostic -InstallDirectory $scratch).MissingFiles.Count -ne 0) { throw 'Existing files reported missing.' }
    $global:DiagnosticTestFailHistory = $true
    $caught = ''
    try { & $diagnostic -InstallDirectory $scratch | Out-Null } catch { $caught = $_.Exception.Message }
    if ($caught -notlike 'Cannot read Defender detection history.*No safety verdict is available.') { throw 'History failure was not explicit.' }
    Write-Output 'Defender diagnostic checks passed: scope, redaction, missing files, empty history and read failure.'
} finally {
    foreach ($name in 'win-mix.exe', 'win-mix.dll') {
        Remove-Item -LiteralPath (Join-Path $scratch $name) -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $scratch
    Remove-Variable DiagnosticTestHistory, DiagnosticTestFailHistory -Scope Global -ErrorAction SilentlyContinue
}
