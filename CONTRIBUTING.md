# Contributing

## Prerequisites

Build and checks require Windows x64 and the exact .NET SDK pinned in [`global.json`](global.json): .NET SDK 10.0.401. Roll-forward is disabled, so install that SDK before running `dotnet` from this checkout. Inno Setup 7.1.0 is needed only to compile the installer.

## Checks and builds

Run these from the repository root in PowerShell:

```powershell
dotnet run --project Tests/GestureTests.csproj -c Release
./Tests/ReleaseChecks.ps1
```

The first command runs deterministic gesture and channel-rule checks without audio hardware. The second checks release version and tag guards; it does not build the app or installer.

To publish the self-contained app without compiling the installer, run the locked publish and verify the published resources and license notices:

```powershell
dotnet publish Mix.csproj -c Release -o publish -p:RestoreLockedMode=true
./Tests/PublishChecks.ps1
```

To publish the self-contained app and compile the installer, install Inno Setup 7.1.0 and pass the path to its `ISCC.exe`:

```powershell
./build.ps1 -Dotnet dotnet -InnoCompiler 'C:\path\to\ISCC.exe'
```

Replace the example compiler path with your local path. The script reruns gesture checks, performs a locked publish, checks published resources and versions, then writes the installer and SHA-256 sidecar to the parent of the checkout by default. `-OutputDirectory` changes that destination. The script replaces the checkout's `publish` directory before publishing, so save any files you need from that directory first.

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

Gesture and selection changes can be checked with `Tests/GestureTests.csproj`. Audio-device and session changes depend on real Windows endpoints; use the probe with care and record any manual checks performed. Do not treat a successful source build as proof of interactive or clean-machine behavior. Existing gaps are recorded in [VERIFICATION.md](VERIFICATION.md).

## Diagnostics

Win Mix stores per-user data under `%LOCALAPPDATA%\Mix.Native`:

- `settings.json` contains saved Windows endpoint IDs, the selected quick-control channel, and whether the shortcut tutorial was dismissed. It does not contain audio levels.
- `last-error.txt` is written for an unhandled application exception and contains exception details.

Before sharing either file or captured probe output, redact device IDs, personal Windows paths, and any other identifying information. The probe can print endpoint details even in its read-only mode. Omit diagnostics you cannot safely sanitize.

## Release

[`Directory.Build.props`](Directory.Build.props) is the single app and installer version source. Use three numeric components, for example `1.0.1`. Before tagging, run the release checks and build the installer with the command above. Commit and push the version change, then push a matching `v` tag:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

Replace `1.0.1` with the version in `Directory.Build.props`. The release workflow checks that the tag matches, builds the self-contained x64 installer, and publishes it with its `.sha256` checksum. The publishing job uses the repository's built-in Actions token.
