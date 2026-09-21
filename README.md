# Win Mix

A small native Windows tray mixer for the existing SteelSeries Sonar Gaming, Chat and Media outputs. Windows 10 version 2004 or newer, x64.

Download the installer from the [latest release](https://github.com/tzachbon/win-mix/releases/latest). Installation is per user. The installer includes the application runtimes. Start with Windows is checked on the first installation. Later installers preserve your preference. Remove Win Mix from Windows Settings > Apps > Installed apps.

Hold **Left Ctrl + Left Alt** to show quick controls at the bottom center of the current monitor. Hover a channel or press Left/Right to select it. Scroll or press Up/Down to change its volume by two percentage points. Press M to toggle mute. Release either modifier to hide the controls. Escape dismisses them. Release the keys before starting another gesture.

The tray menu opens **Mixer**, **Settings**, or exits. Closing the mixer keeps Win Mix running in the tray. Settings lets you choose the Windows output for each channel and the physical headset master output. Missing devices stay unavailable until their exact endpoint returns. Win Mix never changes application routing.

Levels are read from Windows. Muting preserves the volume. Changing volume preserves mute. Settings contain device IDs and the last selected channel, never saved audio levels. Headset dial integration, game-specific overlay compatibility and Sonar's internal mixer synchronization are outside this version.

The main window shows a shortcut tutorial card until you first open the quick controls. That dismissal is remembered. Session rows use the application's Windows icon when available. The generated app icon and its prompt are in `Assets`.

Quick controls place **Main, Game, Chat and Media in one horizontal row**, with extra space after Main. Main controls the physical headset master output selected in Settings. Left/Right navigates Main, Game, Chat, Media. The last selection is retained. The same volume and M shortcuts apply to all four.

The startup toggle also checks Windows' per-app approval marker. An explicit On action clears only Win Mix's marker. Normal launch and upgrades preserve it. The marker format is not a documented Windows API, so malformed or unknown states are conservatively shown as Off.

In Settings, **Check for updates** checks the latest stable GitHub release only when clicked. **Update to…** downloads and verifies the installer, shows installation progress, then reopens Settings with the new version. Downloads can be canceled. Settings and startup preferences are preserved. Development builds link to the release page instead of installing. Version 1.0.1 needs one normal installer upgrade to gain this button.

Updates use GitHub HTTPS release metadata, SHA-256, size and file-version checks. Installers remain unsigned. There are no automatic checks or background downloads. Failed downloads never launch; if the installer itself fails after closing Win Mix, use its error message and reopen the app or rerun the installer.

## Build

Install the .NET SDK version pinned in `global.json` and Inno Setup 7.1.0. From this folder:

```powershell
./build.ps1 -Dotnet dotnet -InnoCompiler 'C:\path\to\ISCC.exe'
```

The script runs the deterministic tests, publishes self-contained application files and creates the installer and SHA-256 sidecar in the parent output folder. Use `-OutputDirectory` to choose another folder. Exact NuGet dependencies are recorded in `packages.lock.json`.

```powershell
dotnet run --project Tests/GestureTests.csproj -c Release
dotnet run --project Tests/UpdateTests/UpdateTests.csproj -c Release
dotnet run --project Tests/AudioProbe/AudioProbe.csproj
```

The audio probe is read-only by default. See its source for the explicit exercise option, which temporarily changes channel levels and restores them in `finally`.

The application uses WinUI 3, Windows Core Audio through NAudio, native tray and input APIs, and the current user's Windows startup registration. No service, driver, account, background updater, or browser runtime is required.

This build is unsigned. Windows may show a reputation warning.

## Release

`Directory.Build.props` is the single version source for the app, Settings and installer. Use three numeric components such as `1.0.1`.

To release, update that file, commit and push the changes, then push a matching tag:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

Replace `1.0.1` with the new version. The `Release installer` workflow rejects mismatched tags, runs tests, builds a self-contained x64 installer and publishes a GitHub release with the EXE and its `.sha256` checksum. It uses the repository's built-in Actions token. No personal access token or separate runtime installation is needed.


