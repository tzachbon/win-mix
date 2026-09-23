# Contributing

## Prerequisites

Build and checks require Windows x64 and the exact .NET SDK pinned in [`global.json`](global.json): .NET SDK 10.0.401. Roll-forward is disabled, so install that SDK before running `dotnet` from this checkout. Inno Setup 7.1.0 is needed only to compile the installer.

## Checks and builds

Run these from the repository root in PowerShell:

```powershell
dotnet run --project Tests/GestureTests.csproj -c Release
dotnet run --project Tests/UpdateTests/UpdateTests.csproj -c Release
./Tests/ReleaseChecks.ps1
./Tests/DefenderChecks.ps1
./Tests/DefenderDiagnosticChecks.ps1
```

The first command runs deterministic gesture and channel-rule checks without audio hardware. The second runs the updater test suite. The release and Defender checks exercise version guards, scan failures, changed artifacts, and evidence cleanup with harmless fixtures.

With Inno Setup available, run `./Tests/InstallerShutdownChecks.ps1 -InnoCompiler 'C:\path\to\ISCC.exe'`. It compiles the real installer's shutdown code into an isolated temporary installer with harmless processes. It checks damaged or missing apps, graceful exit, failed shutdown, unknown process state, unrelated installations, and file locks. It creates no startup entries or uninstall registration and does not install Win Mix. This is regression coverage, not a substitute for real app updates on Windows 10 and 11.

To publish the self-contained app without compiling the installer, run the locked publish and verify the published resources and license notices:

```powershell
dotnet publish Mix.csproj -c Release -o publish -p:RestoreLockedMode=true
./Tests/PublishChecks.ps1
```

To publish the self-contained app and compile the installer, install Inno Setup 7.1.0 and pass the path to its `ISCC.exe`:

```powershell
./build.ps1 -Dotnet dotnet -InnoCompiler 'C:\path\to\ISCC.exe'
```

Replace the example compiler path with your local path. The script reruns gesture and updater checks, performs a locked publish, checks published resources and versions, then writes the installer and SHA-256 checksum. Builds and CI do not request Defender scans by default. Windows real-time protection remains unchanged.

To explicitly scan the payload and installer, add `-ScanWithDefender` to the build command. This requires Defender in normal mode with current signatures updated within 48 hours. Detection, scan failure, or changed files aborts that build. The opt-in scan writes `.defender.json` evidence and `*-scan.log` output using `-DisableRemediation`, which does not disable real-time protection. Every build removes stale scan evidence, including builds without scans. Do not add exclusions or restore quarantined files to make a build pass. A clean scan applies only to the exact bytes and recorded definitions, not future definitions or an unavailable quarantined file.

Output goes to the parent of the checkout by default. `-OutputDirectory` changes that destination. The script replaces the checkout's `publish` directory before publishing, so save any files you need from that directory first.

The audio probe is separate from the deterministic checks. Its default mode reads endpoint and session state without changing levels:

```powershell
dotnet run --project Tests/AudioProbe/AudioProbe.csproj
```

The explicit exercise mode changes live endpoint and app-session levels temporarily and attempts to restore them. Use it only when temporary audio changes are acceptable:

```powershell
dotnet run --project Tests/AudioProbe/AudioProbe.csproj -- --exercise
```

## Source map

- `Core/` contains gesture handling and channel-selection rules, suitable for deterministic tests.
- `Audio/` enumerates Windows audio endpoints and app sessions, observes their changes, and controls their levels and mute state.
- `Platform/` contains native Windows input/tray hosting, single-instance handling, saved preferences, and startup registration.
- `UI/` contains the WinUI mixer, settings, quick-controls overlay, and app icons.

Gesture and selection changes can be checked with `Tests/GestureTests.csproj`. Audio-device and session changes depend on real Windows endpoints; use the probe with care and record any manual checks performed. Do not treat a successful source build as proof of interactive or clean-machine behavior. Existing gaps are recorded in [open-source readiness verification](docs/research/open-source-readiness-verification.md).

## Diagnostics

For a local report when Win Mix fails to start, run:

```powershell
./diagnose-defender.ps1 | ConvertTo-Json -Depth 4
```

This manual, read-only check reports missing `win-mix.exe`/`win-mix.dll` files and existing Defender file detections under `%LOCALAPPDATA%\Programs\Mix.Native`. Use `-InstallDirectory 'C:\path\to\Win Mix'` for a different installation. It works even when the app cannot start. It uses [Get-MpThreatDetection](https://learn.microsoft.com/en-us/powershell/module/defender/get-mpthreatdetection) and does not start a scan, upload files, restore quarantine or change protection. A history read failure is reported as an error, not an empty result.

History includes past detections, and `ActionSuccess` describes the recorded action, not current safety. No matching records is not a clean bill of health, and missing files alone do not prove quarantine. Only matching `file:_` resources are included, with paths relative to the selected installation. Check Windows Security > Protection history for current details. This diagnostic does not fix or establish the cause of an antivirus detection.

Win Mix stores per-user data under `%LOCALAPPDATA%\Mix.Native`:

- `settings.json` contains saved Windows endpoint IDs, the selected quick-control channel, and whether the shortcut tutorial was dismissed. It does not contain audio levels.
- `last-error.txt` is written for an unhandled application exception and contains exception details.

Before sharing either file or captured probe output, redact device IDs, personal Windows paths, and any other identifying information. The probe can print endpoint details even in its read-only mode. Omit diagnostics you cannot safely sanitize.

## PRs and releases

Use a Conventional Commit PR title, for example `fix: preserve mute state`. Normal PRs are manually squash merged after required checks. See [release policy and recovery](docs/releases.md) for version bumps and the paused release pipeline. `VERSION` is the single application version source. Do not push release tags manually.

Release policy and publication tests require Node.js 24:

```powershell
node --test Tests/*.test.cjs Tests/site.test.mjs
```
