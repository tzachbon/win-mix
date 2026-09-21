# Verification

Status: PARTIAL acceptance. Build, audio readback and installation lifecycle passed on the development machine. Clean-machine and final interactive checks remain below.

Measured on 2026-09-21:

| Check | Evidence |
| --- | --- |
| Deterministic input | `dotnet run --project Tests/GestureTests.csproj -c Release` passed. Covers modifier order, AltGr/injected exclusion, neutral rearming, cancellation, matching key-up ownership, mute repeat, wheel remainder, clamp and ambiguous endpoint discovery. |
| Endpoint writes | AudioProbe `--exercise` changed Game, Chat and Media independently, read back each change and verified the other channels stayed unchanged. All exercised levels restored in finally. |
| Mute | Endpoint and session mute retained volume, verified through Windows audio readback. |
| App sessions | A probe-owned silent session appeared through the session-created notification. Its independent volume and mute readback passed. External session-volume changes appeared. |
| External endpoint volume | External Windows volume writes appeared through notifications. |
| Missing endpoint | A synthetic missing endpoint ID became unavailable without fallback. Rebinding the original ID recovered. This is not physical unplug/reconnect evidence. |
| Native mixer | An earlier native slider interaction changed only Game to 98%, confirmed with independent Windows readback. Restored afterward. User confirmed the original app worked. |
| Published runtime | Published executable started with no main window and exited through `--shutdown`. Application PRI and XBF files are explicitly copied to publish to avoid the .NET 10 packaging omission. |
| Background and instance | Background launch had MainWindowHandle=0. Second normal launch opened the existing process. Graceful shutdown exited successfully. |
| Self-contained installed runtime | Installed process loaded coreclr.dll and Microsoft.UI.Xaml.dll from its own per-user application folder. |
| Standard-user install | Unelevated token, installer exit 0, per-user files, Start menu link, Windows uninstall entry and checked startup default verified. |
| Upgrade | Installer stopped the running app, preserved the exact settings hash and preserved absent startup registration. |
| Uninstall | Exit 0, app stopped, settings, shortcut, startup and uninstall entry removed. A deliberately unrelated file inside the install directory survived. |
| Busy instance | A held instance marker made silent installer exit 7 before changing the installed executable hash. Interactive Retry/Cancel remains untested. |
| Final local state | Reinstalled and restored the user's device preferences. Current audio levels and Sonar routing were not restored from settings or changed by installation. |
| Shortcut onboarding | User confirmed the bottom-center popup appears and the tutorial card disappears on first use. |

## Remaining acceptance checks

- Clean Windows environment with no development tools. Windows Sandbox is unavailable here.
- Latest native appearance, actual app icons, light/dark theme, keyboard accessibility and multiple DPI/monitor layouts need interactive confirmation.
- Selected-channel M mute and tutorial dismissal after a later restart need final interactive confirmation. First-use dismissal and popup placement were confirmed by the user.
- Physical device unplug/reconnect and real session expiration/removal.
- Sign-in launch and interactive installer Retry/Cancel.
- Independent post-implementation review pending.

## Acceptance boundary

Desktop interaction, Windows endpoint/session volume readback, and per-user installation lifecycle are required. Headset dial integration, individual games, and Sonar internal mixer synchronization are deferred.

## Environment

Windows x64, OS build 26200. .NET SDK 10.0.401 is task-local. Windows App SDK 2.5.1 and NAudio.Wasapi 2.2.1 are pinned. Inno Setup compiler 7.1.0 was obtained from the official release and its installer Authenticode signature validated.

Windows Sandbox is not installed on the development machine. Clean-machine acceptance must be distinguished from tests on this machine.

## References

- [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
- [Self-contained unpackaged Windows App SDK deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)
- [Windows kernel object namespaces](https://learn.microsoft.com/en-us/windows/win32/termserv/kernel-object-namespaces)
- [Inno Setup per-user installation](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)
