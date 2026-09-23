param(
    [ValidateNotNullOrEmpty()][string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs/Mix.Native')
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\', '/') + '\'
$missing = @('win-mix.exe', 'win-mix.dll' | Where-Object {
    !(Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf)
})
try { $history = @(Get-MpThreatDetection -ErrorAction Stop) }
catch { throw 'Cannot read Defender detection history. Check Windows Security or retry in an elevated PowerShell window. No safety verdict is available.' }
$detections = @(foreach ($record in $history) {
    $files = @(foreach ($resource in $record.Resources) {
        if ($resource -notmatch '^file:_(.+)$') { continue }
        $path = $Matches[1]
        if ($path -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\]+\\[^\\]+)') { continue }
        try { $path = [IO.Path]::GetFullPath($path) } catch { continue }
        if ($path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            $path.Substring($root.Length)
        }
    })
    if ($files.Count) {
        [pscustomobject]@{
            DetectedAt = $record.InitialDetectionTime
            ThreatID = $record.ThreatID
            ActionSuccess = $record.ActionSuccess
            Files = $files
        }
    }
})
[pscustomobject]@{
    History = if ($detections.Count) { 'Recorded matching detections found.' } else { 'No recorded matching detections.' }
    Note = 'History is not a current safety verdict. Missing files do not establish a cause. Review Windows Security > Protection history.'
    MissingFiles = $missing
    Detections = $detections
}
