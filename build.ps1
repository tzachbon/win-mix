param(
    [string]$Dotnet = 'dotnet',
    [string]$InnoCompiler = 'ISCC.exe'
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & $Dotnet run --project Tests/GestureTests.csproj -c Release
    if ($LASTEXITCODE) { throw 'Gesture tests failed.' }
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
    & $InnoCompiler installer/Mix.iss
    if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
} finally { Pop-Location }
