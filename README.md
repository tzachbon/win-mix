# Win Mix

A small native Windows tray mixer for the existing SteelSeries Sonar Gaming, Chat and Media outputs. Windows 10 version 2004 or newer, x64.

Run `win-mix-Setup-1.0.0-x64.exe`. Installation is per user. The installer includes the application runtimes. Start with Windows is checked on the first installation. Later installers preserve your preference. Remove Win Mix from Windows Settings > Apps > Installed apps.

Hold **Left Ctrl + Left Alt** to show quick controls at the bottom center of the current monitor. Hover a channel or press Left/Right to select it. Scroll or press Up/Down to change its volume by two percentage points. Press M to toggle mute. Release either modifier to hide the controls. Escape dismisses them. Release the keys before starting another gesture.

The tray menu opens **Mixer**, **Settings**, or exits. Closing the mixer keeps Win Mix running in the tray. Settings lets you choose the Windows output for each channel and the physical headset master output. Missing devices stay unavailable until their exact endpoint returns. Win Mix never changes application routing.

Levels are read from Windows. Muting preserves the volume. Changing volume preserves mute. Settings contain device IDs and the last selected channel, never saved audio levels. Headset dial integration, game-specific overlay compatibility and Sonar's internal mixer synchronization are outside this version.

## Build

Install the .NET SDK version pinned in `global.json` and Inno Setup 7.1.0. From this folder:

```powershell
./build.ps1 -Dotnet dotnet -InnoCompiler 'C:\path\to\ISCC.exe'
```

The script runs the deterministic tests, publishes self-contained application files and creates the installer in the parent output folder. Exact NuGet dependencies are recorded in `packages.lock.json`.

```powershell
dotnet run --project Tests/GestureTests.csproj -c Release
dotnet run --project Tests/AudioProbe/AudioProbe.csproj
```

The audio probe is read-only by default. See its source for the explicit exercise option, which temporarily changes channel levels and restores them in `finally`.

The application uses WinUI 3, Windows Core Audio through NAudio, native tray and input APIs, and the current user's Windows startup registration. No service, driver, account, updater, or browser runtime is required.

This private build is unsigned. Windows may show a reputation warning.


