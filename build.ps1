param(
    [string]$Dotnet = 'dotnet',
    [string]$InnoCompiler = 'ISCC.exe',
    [string]$OutputDirectory = (Split-Path $PSScriptRoot -Parent),
    [string]$ExpectedTag
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $version = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'VERSION')).Trim()
    if ($version -cnotmatch '^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$' -or
        @($version.Split('.') | Where-Object { [int]$_ -gt 65535 }).Count) {
        throw 'Version must contain three integers from 0 to 65535 without leading zeros.'
    }
    if ($ExpectedTag -and $ExpectedTag -cne "v$version") { throw "Tag must be v$version." }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
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
    & (Join-Path $PSScriptRoot 'Tests/PublishChecks.ps1') -PublishDirectory $publishPath
    $appVersion = (Get-Item 'publish/win-mix.dll').VersionInfo.ProductVersion.Split('+')[0]
    if ($appVersion -ne $version) { throw "Application version mismatch: $appVersion" }
    & $InnoCompiler "/DAppVersion=$version" "/O$OutputDirectory" installer/Mix.iss
    if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
    $installer = Join-Path $OutputDirectory "win-mix-Setup-$version-x64.exe"
    if ((Get-Item $installer).VersionInfo.FileVersion.Trim() -ne "$version.0") { throw 'Installer version mismatch.' }
    $hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$installer.sha256", "$hash  $([IO.Path]::GetFileName($installer))`n")
} finally { Pop-Location }
