param(
    [string]$Dotnet = 'dotnet',
    [string]$InnoCompiler = 'ISCC.exe',
    [string]$OutputDirectory = (Split-Path $PSScriptRoot -Parent),
    [string]$ExpectedTag
)
$ErrorActionPreference = 'Stop'
$sidecars = @()
Push-Location $PSScriptRoot
try {
    [xml]$properties = Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw
    $version = [string]$properties.Project.PropertyGroup.Version
    if ($version -cnotmatch '^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$' -or
        @($version.Split('.') | Where-Object { [int]$_ -gt 65535 }).Count) {
        throw 'Version must contain three integers from 0 to 65535 without leading zeros.'
    }
    if ($ExpectedTag -and $ExpectedTag -cne "v$version") { throw "Tag must be v$version." }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    $installer = Join-Path $OutputDirectory "win-mix-Setup-$version-x64.exe"
    # Remove previous success markers so a failed retry cannot leave stale evidence.
    $sidecars = @("$installer.sha256", "$installer.defender.json", "$installer.defender.json.tmp")
    foreach ($sidecar in $sidecars) {
        if (Test-Path -LiteralPath $sidecar) { Remove-Item -LiteralPath $sidecar -Force }
    }
    & $Dotnet run --project Tests/GestureTests.csproj -c Release
    if ($LASTEXITCODE) { throw 'Gesture tests failed.' }
    & $Dotnet run --project Tests/UpdateTests/UpdateTests.csproj -c Release
    if ($LASTEXITCODE) { throw 'Update tests failed.' }
    $publishPath = Join-Path $PSScriptRoot 'publish'
    if (Test-Path -LiteralPath $publishPath) {
        $existing = Get-Item -LiteralPath $publishPath -Force
        if ($existing.FullName -ne [IO.Path]::GetFullPath($publishPath) -or
            ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe publish directory.' }
        Remove-Item -LiteralPath $publishPath -Recurse -Force
    }
    & $Dotnet publish Mix.csproj -c Release -o publish -p:RestoreLockedMode=true
    if ($LASTEXITCODE) { throw 'Publish failed.' }
    foreach ($resource in 'win-mix.pri', 'App.xbf', 'Assets/app.ico', 'Assets/app-icon.png') {
        if (!(Test-Path (Join-Path publish $resource))) { throw "Missing published resource: $resource" }
    }
    $appVersion = (Get-Item 'publish/win-mix.dll').VersionInfo.ProductVersion.Split('+')[0]
    if ($appVersion -ne $version) { throw "Application version mismatch: $appVersion" }
    $applicationScan = & (Join-Path $PSScriptRoot 'scan-release.ps1') -Path $publishPath
    & $InnoCompiler "/DAppVersion=$version" "/O$OutputDirectory" installer/Mix.iss
    if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
    if ((Get-Item $installer).VersionInfo.FileVersion.Trim() -ne "$version.0") { throw 'Installer version mismatch.' }
    $installerScan = & (Join-Path $PSScriptRoot 'scan-release.ps1') -Path $installer
    $hash = $installerScan.Files[0].SHA256.ToLowerInvariant()
    [IO.File]::WriteAllText("$installer.sha256", "$hash  $([IO.Path]::GetFileName($installer))`n")
    $evidence = @{ Application = $applicationScan; Installer = $installerScan } | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText("$installer.defender.json.tmp", $evidence)
    Move-Item -LiteralPath "$installer.defender.json.tmp" -Destination "$installer.defender.json"
} catch {
    foreach ($sidecar in $sidecars) { Remove-Item -LiteralPath $sidecar -Force -ErrorAction SilentlyContinue }
    throw
} finally { Pop-Location }
