$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) "win-mix-release-check-$([Guid]::NewGuid())"
New-Item $scratch -ItemType Directory | Out-Null
try {
    Copy-Item (Join-Path $PSScriptRoot '../build.ps1') $scratch
    foreach ($version in '1.0.1', '01.0.1', '1.0', '1.0.65536', '1.0.1-preview', '1.0.1;bad') {
        Set-Content (Join-Path $scratch 'Directory.Build.props') "<Project><PropertyGroup><Version>$version</Version></PropertyGroup></Project>"
        $expected = if ($version -eq '1.0.1') { 'Tag must be v1.0.1.' } else { 'Version must contain three integers from 0 to 65535 without leading zeros.' }
        $message = ''
        try { & (Join-Path $scratch 'build.ps1') -ExpectedTag 'v9.9.9' -Dotnet 'must-not-run' }
        catch { $message = $_.Exception.Message }
        if ($message -cne $expected) { throw "Release guard failed for '$version': $message" }
    }
    Write-Output 'Release version/tag guards passed before build execution.'
} finally {
    # The random scratch directory is created above and contains only these test files.
    Remove-Item -LiteralPath (Join-Path $scratch 'build.ps1') -Force
    Remove-Item -LiteralPath (Join-Path $scratch 'Directory.Build.props') -Force
    Remove-Item -LiteralPath $scratch
}
