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
| Final package | Clean build and installer compilation passed at commit 364a883. A planted obsolete file was removed before packaging in the earlier regression check. Final installed app launched successfully and preserved the settings hash. |
| Main selection | Pure tests verify Main → Game → Chat → Media order and clamps at both ends. Main uses the existing Master endpoint key. Hover uses each measured card rectangle. |
| Startup override | Parser tests cover missing, enabled, disabled, unknown and malformed approval. A scoped registry probe verified disabled readback, explicit re-enable and disable, then restored the original app-owned values in finally. |

Final installer SHA-256: `A14EAB3A602C7FC155D09F01E6BCD59BFFB639ADF4552819377C37A1CE4E3C37`.

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

Horizontal layout dbe6f2f: Main, Game, Chat and Media share one row, with extra space after Main. Clean build and installer passed, settings were preserved, and the installed process started quietly. Final popup appearance remains a manual check.
## Release 1.0.1 (2026-09-21)

- Shared version source: `Directory.Build.props`. App informational version is `1.0.1+5117b36bfb5bada24e9bf8624ba018a089ebce0c`; app and installer file versions are `1.0.1.0`.
- Fresh independent planning and post-implementation reviews passed. Post-review covered `77f60c0..5117b36` with no P0-P2 findings.
- Local clean build, gesture tests, release version/tag guard checks, binary metadata checks and `sha256sum --check` passed.
- [GitHub Actions run 35643636732](https://github.com/tzachbon/win-mix/actions/runs/35643636732) built and published [v1.0.1](https://github.com/tzachbon/win-mix/releases/tag/v1.0.1) from `5117b36bfb5bada24e9bf8624ba018a089ebce0c`. Both jobs passed. Release has exactly the installer and checksum sidecar.
- Released installer SHA-256: `03c9d745197c453f65e54d02686eb546d62f5587cefd5e6609c3995464f2f2c5`.
- Downloaded the hosted installer, verified its checksum/version, and upgraded the existing per-user installation. Exit code 0; settings hash, startup registration and startup approval were preserved. Installed version matched; exactly one process remained after a quiet background launch with `MainWindowHandle = 0`.
- Made the repository public after release verification. Unauthenticated repository API access and downloads of both release assets succeeded. Public installer bytes matched the tested hosted installer and sidecar.
- This validates the release workflow and upgrade on the development machine. The earlier clean-machine and manual UI acceptance gaps remain unchanged.

## Open-source readiness implementation (2026-09-21)

Status: PARTIAL product acceptance. Repository changes implement T1-T4 of the [readiness plan](docs/plans/open-source-readiness.md). T5 clean-machine and outstanding interactive scenarios remain unverified.

| Check | Evidence |
| --- | --- |
| Licensing and provenance | Root MIT license, sole recorded Git author `tzachbon`, generated-icon prompt retained. [Attribution inventory](Licenses/README.md) identifies bundled components and adds missing package-supplied transitive notices. |
| Local deterministic checks | SDK 10.0.401: `dotnet run --project Tests/GestureTests.csproj -c Release` and `./Tests/ReleaseChecks.ps1` passed. |
| Locked publish | `dotnet publish Mix.csproj -c Release -o publish -p:RestoreLockedMode=true` passed. `./Tests/PublishChecks.ps1` verified the four runtime resources and all 15 license/attribution files byte-for-byte. A scratch copy with a deliberately corrupted LICENSE was rejected. |
| Installer compilation | `build.ps1` passed with Inno Setup 7.1.0. Compiler output listed root LICENSE and every Licenses file as compressed into the installer. SHA-256: `39db9e4d49ca785badcf5f9288d2c28a48f26a0e99d382c907cddabe56839ecc`. This package was not installed or released. |
| Genuine visual | [Mixer screenshot](docs/images/mixer.png) captured from installed 1.0.1 on Windows 11 build 26200. Window-only image inspected for private content. This confirms the displayed mixer layout, not quick-control keyboard/mute or multi-DPI behavior. |
| Workflow boundary | New CI uses read-only permissions and credential-free checkout for PRs and pushes to main. The tag-release workflow is unchanged. Implementation `2ab6e55` passed [run 35648027204](https://github.com/tzachbon/win-mix/actions/runs/35648027204). A deliberate failing assertion on disposable PR #4 at `79c9f492fb9470e0fca2a36cf8d9a90b0884c76a` failed [run 35648037114](https://github.com/tzachbon/win-mix/actions/runs/35648037114) with exit 1. Removing it in `bc3121a1c3add32ed669c9d702aee1d246103a42` restored the exact implementation tree and passed [run 35648241275](https://github.com/tzachbon/win-mix/actions/runs/35648241275). The disposable PR was closed without merging. |
| Reporting | Issue and PR templates added. Both issue forms rendered correctly in GitHub's branch-file Preview, including required fields. After owner approval, private vulnerability reporting was enabled and the repository API returned `enabled: true`. The signed-in `/security/advisories/new` page displayed its private advisory form; no report was submitted. [SECURITY.md](SECURITY.md) documents the private route, latest-release scope and diagnostic redaction. |
| Independent review | Separate fresh-context planning and post-change reviews in isolated checkouts passed. The post-change review covered implementation `2ab6e55` with no P0-P2 findings. |

Windows Sandbox is absent on this host, and the current shell lacks permission to enumerate Hyper-V VMs. Owner approval does not remove those environment limits. No fresh clean-machine, physical unplug/reconnect, sign-in, interactive installer Retry/Cancel, theme, or multi-monitor/DPI result is claimed. Earlier runtime evidence above remains historical. Build/notice checks do not close these acceptance gaps.
