param([string]$PublishDirectory = (Join-Path $PSScriptRoot '../publish'))
$ErrorActionPreference = 'Stop'
foreach ($resource in 'win-mix.pri', 'App.xbf', 'Assets/app.ico', 'Assets/app-icon.png') {
    if (!(Test-Path -LiteralPath (Join-Path $PublishDirectory $resource) -PathType Leaf)) {
        throw "Missing published resource: $resource"
    }
}
$sourceRoot = Split-Path $PSScriptRoot -Parent
$licenses = @('LICENSE') + @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Licenses') -File | ForEach-Object { "Licenses/$($_.Name)" })
foreach ($license in $licenses) {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $sourceRoot $license) -Algorithm SHA256).Hash
    $publishedHash = (Get-FileHash -LiteralPath (Join-Path $PublishDirectory $license) -Algorithm SHA256).Hash
    if ($sourceHash -ne $publishedHash) { throw "Published license differs: $license" }
}
Write-Output "Published resources and $($licenses.Count) license files verified."
