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
| Onboarding persistence | Saved HasUsedShortcut=true survived the later installer updates and process restart. |
| Final package | Clean build and installer compilation passed at commit bd8e3ce. A planted obsolete file was removed before packaging in the earlier regression check. Final installed app launched successfully and preserved the settings hash. |
| Main selection | Pure tests verify Main → Game → Chat → Media order and clamps at both ends. Main uses the existing Master endpoint key. Hover uses each measured card rectangle. |
| Startup override | Parser tests cover missing, enabled, disabled, unknown and malformed approval. A scoped registry probe verified disabled readback, explicit re-enable and disable, then restored the original app-owned values in finally. |

Final installer SHA-256: `BEC70ED914CEC47E6DC6D858E8D3CDE97BC751EAE0257418F7AF776DB37DE44B`.

## Remaining acceptance checks

- Clean Windows environment with no development tools. Windows Sandbox is unavailable here.
- Latest native appearance, actual app icons, light/dark theme, keyboard accessibility and multiple DPI/monitor layouts need interactive confirmation.
- Selected-channel M mute needs final interactive confirmation. First-use dismissal and popup placement were confirmed by the user. Saved dismissal survived restart.
- Physical device unplug/reconnect and real session expiration/removal.
- Sign-in launch and interactive installer Retry/Cancel.
- New Main-row hover/keyboard/mute integration and latest clipping fix await visual confirmation.

## Independent review

Separate fresh-context Sol xhigh review in an isolated Git worktree passed code at 877d84c after fixes for stale publish files and Windows startup approval handling. No remaining code blocker was found. This is separate from the outstanding product acceptance checks above.

Windows' StartupApproved registry format is undocumented. The app recognizes the observed 12-byte enabled states 2 and 6, treats unknown values as off, and only clears its own marker when the user explicitly enables startup. Normal launches and upgrades do not reset it. [First-hand Windows registry observations](https://windowsir.blogspot.com/2022/07/startupapprovedrun-pt-ii.html) describe the independent approval marker and its lifecycle.

## Acceptance boundary

Desktop interaction, Windows endpoint/session volume readback, and per-user installation lifecycle are required. Headset dial integration, individual games, and Sonar internal mixer synchronization are deferred.

## Environment

Windows x64, OS build 26200. .NET SDK 10.0.401 is task-local. Windows App SDK 2.5.1 and NAudio.Wasapi 2.2.1 are pinned. Inno Setup compiler 7.1.0 was obtained from the official release and its installer Authenticode signature validated.

Windows Sandbox is not installed on the development machine. Clean-machine acceptance must be distinguished from tests on this machine.

## References

- [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
- [Native WinUI TitleBar control](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/title-bar)
- [Self-contained unpackaged Windows App SDK deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)
- [Windows kernel object namespaces](https://learn.microsoft.com/en-us/windows/win32/termserv/kernel-object-namespaces)
- [Inno Setup per-user installation](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)

Branding update bd8e3ce: popup padding and both header icons built and installed successfully. The PNG is indexed in the PRI, and its installed hash matches the source asset. Preferences were preserved. Final appearance remains a manual check.
