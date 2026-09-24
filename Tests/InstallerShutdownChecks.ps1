param([Parameter(Mandatory)][string]$InnoCompiler)
$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) "win-mix-shutdown-check-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $scratch | Out-Null
$source = Get-Content (Join-Path $PSScriptRoot '../installer/Mix.iss') -Raw
$code = $source.Substring($source.IndexOf('[Code]') + 6)
$fixtureSource = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
class Fixture {
    static int Main(string[] args) {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string name = "Local\\WinMixShutdownTest." + File.ReadAllText(Path.Combine(dir, "event"));
        if (Array.IndexOf(args, "--shutdown") >= 0) {
            File.WriteAllText(Path.Combine(dir, "shutdown-called"), "yes");
            string mode = File.ReadAllText(Path.Combine(dir, "mode"));
            if (mode == "stubborn") return 1;
            using (var signal = EventWaitHandle.OpenExisting(name)) signal.Set();
            try { Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(dir, "ready")))).WaitForExit(5000); }
            catch (ArgumentException) { }
            return mode == "nonzero" ? 9 : 0;
        }
        using (var signal = new EventWaitHandle(false, EventResetMode.ManualReset, name)) {
            File.WriteAllText(Path.Combine(dir, "ready"), Process.GetCurrentProcess().Id.ToString());
            signal.WaitOne(60000);
        }
        return 0;
    }
}
'@
Set-Content -LiteralPath (Join-Path $scratch 'fixture.cs') -Value $fixtureSource
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:winexe "/out:$scratch/win-mix.exe" (Join-Path $scratch 'fixture.cs')
if ($LASTEXITCODE) { throw 'Fixture compilation failed.' }
Set-Content (Join-Path $scratch 'payload.txt') 'installed'
function Compile-Installer([string]$Name, [string]$Code) {
    $script = @"
[Setup]
AppId=WinMixShutdownCheck-$([guid]::NewGuid())
AppName=Win Mix shutdown check
AppVersion=1.0.0
DefaultDirName=$scratch\unused
UsePreviousAppDir=no
PrivilegesRequired=lowest
Uninstallable=no
CreateUninstallRegKey=no
OutputDir=$scratch
OutputBaseFilename=$Name
[Files]
Source: "$scratch\payload.txt"; DestDir: "{app}"; Flags: ignoreversion
[Code]
$Code
"@
    $path = Join-Path $scratch "$Name.iss"
    Set-Content -LiteralPath $path -Value $script
    & $InnoCompiler /Q $path
    if ($LASTEXITCODE) { throw "Installer fixture compilation failed: $Name" }
    Join-Path $scratch "$Name.exe"
}
function Run-Installer([string]$Installer, [string]$Directory, [bool]$Pass, [string]$Message = '') {
    $log = Join-Path $scratch "$([guid]::NewGuid()).log"
    $process = Start-Process -FilePath $Installer -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /DIR=`"$Directory`" /LOG=`"$log`"" -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(20000)) { throw "Installer timed out. PID $($process.Id), log $log" }
    $payload = Join-Path $Directory 'payload.txt'
    $installed = (Test-Path -LiteralPath $payload) -and ((Get-Content -LiteralPath $payload -Raw).Trim() -eq 'installed')
    if (($process.ExitCode -eq 0) -ne $Pass -or $installed -ne $Pass) {
        throw "Unexpected installer result: exit=$($process.ExitCode), installed=$installed, expected=$Pass. Log: $log"
    }
    if ($Message -and (Get-Content -LiteralPath $log -Raw) -notlike "*$Message*") { throw "Missing diagnostic '$Message' in $log" }
}
function New-Target([string]$Name) {
    $directory = Join-Path $scratch $Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    $directory
}
function Start-Fixture([string]$Directory, [string]$Mode) {
    Copy-Item -LiteralPath (Join-Path $scratch 'win-mix.exe') -Destination $Directory
    [IO.File]::WriteAllText((Join-Path $Directory 'event'), [guid]::NewGuid().ToString())
    [IO.File]::WriteAllText((Join-Path $Directory 'mode'), $Mode)
    $process = Start-Process -FilePath (Join-Path $Directory 'win-mix.exe') -WindowStyle Hidden -PassThru
    for ($retry = 0; $retry -lt 100; $retry++) {
        if (Test-Path (Join-Path $Directory 'ready')) {
            if ($process.MainWindowHandle -ne 0) { throw 'Fixture must run without a window.' }
            return $process
        }
        Start-Sleep -Milliseconds 50
    }
    throw 'Fixture failed to become ready.'
}
function Stop-Fixture([string]$Directory, $Process) {
    if (!$Process.HasExited) {
        $signal = [Threading.EventWaitHandle]::OpenExisting('Local\WinMixShutdownTest.' + [IO.File]::ReadAllText((Join-Path $Directory 'event')))
        try { $signal.Set() | Out-Null } finally { $signal.Dispose() }
        if (!$Process.WaitForExit(5000)) { throw 'Fixture failed to exit.' }
    }
}
$passed = $false
try {
    $installer = Compile-Installer 'setup' $code
    Run-Installer $installer (New-Target 'missing-exe') $true
    $directory = New-Target 'damaged-app'
    Set-Content (Join-Path $directory 'win-mix.exe') 'harmless invalid executable'
    Run-Installer $installer $directory $true
    Write-Output 'PASS: missing and damaged stopped applications do not require shutdown execution.'
    foreach ($mode in 'healthy', 'nonzero', 'stubborn') {
        $directory = New-Target $mode
        $process = Start-Fixture $directory $mode
        try {
            $message = if ($mode -eq 'stubborn') { 'shutdown command failed and Win Mix is still running' } else { '' }
            Run-Installer $installer $directory.ToUpperInvariant() ($mode -ne 'stubborn') $message
            if (!(Test-Path (Join-Path $directory 'shutdown-called'))) { throw 'Shutdown was not requested.' }
            if ($mode -eq 'stubborn' -and $process.HasExited) { throw 'Stubborn process was terminated.' }
        } finally { Stop-Fixture $directory $process }
    }
    Write-Output 'PASS: healthy shutdown, nonzero exit after shutdown, and a still-running app.'
    $other = New-Target 'unrelated-aa'
    $process = Start-Fixture $other 'stubborn'
    try {
        Run-Installer $installer (New-Target 'other-target') $true
        if ($process.HasExited -or (Test-Path (Join-Path $other 'shutdown-called'))) { throw 'Unrelated process was touched.' }
        $unknown = Compile-Installer 'unknown' ($code.Replace('Processes.ItemIndex(I).ExecutablePath', 'Null'))
        Run-Installer $unknown (New-Target 'unknown-target') $false 'Could not verify'
    } finally { Stop-Fixture $other $process }
    $unavailable = Compile-Installer 'unavailable' ($code.Replace('WbemScripting.SWbemLocator', 'WinMix.Test.MissingProvider'))
    Run-Installer $unavailable (New-Target 'unavailable-target') $false 'Could not verify'
    Write-Output 'PASS: unrelated processes stay running; unknown paths and failed queries block installation.'
    $directory = New-Target 'locked-file'
    $payload = Join-Path $directory 'payload.txt'
    [IO.File]::WriteAllText($payload, 'original')
    $locked = [IO.File]::Open($payload, 'Open', 'Read', 'Read')
    try {
        Run-Installer $installer $directory $false
        if ([IO.File]::ReadAllText($payload) -cne 'original') { throw 'Locked file was replaced.' }
    } finally { $locked.Dispose() }
    Write-Output 'PASS: file locks still block replacement after the process check.'
    $passed = $true
} finally {
    if ($passed) {
        $item = Get-Item -LiteralPath $scratch
        if ($item.Parent.FullName -ne [IO.Path]::GetTempPath().TrimEnd('\', '/') -or
            $item.Name -notlike 'win-mix-shutdown-check-*' -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe fixture cleanup path.' }
        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    } else { Write-Output "Failure evidence retained: $scratch" }
}
