# Move app audio between channels

## Outcome and acceptance

The user wants to open Settings and move an app's audio between Game, Chat, and Media using a button or selector. A channel selector is the smallest complete interaction. This changes the app's Windows playback preference, not merely its position in the mixer.

- R1: Settings lists applications with current audio sessions on every active render endpoint and provides Game, Chat, and Media destinations. The user explicitly requires both running and not-running apps. Configured inactive apps appear under a collapsed `Not running (n)` Expander. `Add app...` opens an executable picker, allowing configuration before first launch. Adding an executable alone does not assign a channel or change routing. Choosing Game, Chat, or Media saves an explicit assignment.
- R2: Selecting a channel durably saves a per-executable assignment in existing local preferences. For a running supported app, apply it immediately through the Windows per-app render preference. For an inactive app or unavailable destination, apply it on the next eligible session/device notification. Reapply to new processes after restart. Observe actual session migration before showing the current output as changed. Explain that apps with explicit in-app outputs may require Default output or playback restart. Multiple sessions/processes sharing one verified executable path are one Settings app, and the assignment applies to each supported live PID.
- R3: Routing cannot change endpoint volume, mute, capture routing, global defaults, startup behavior, or unrelated apps. Do not offer System sounds as a routable application. Reject expired sessions and unavailable destinations.
- R4: Existing mixer controls continue working. Report routing failures visibly and retain the observed output until Windows reports a change. Restore prior role preferences on a partial failure where possible and report rollback failure truthfully.
- R5: Leave a practical deterministic regression check and an opt-in, isolated live routing probe. Validate Settings with the actual WinUI controls. Do not route a user's existing app for testing.
- R6: Create a GitHub issue and non-draft PR linked using closing syntax. Finish only at provider-verified MERGE READY: current head acceptance passes, PR open and mergeable, required checks successful, review requirements satisfied, zero unresolved conversations or actionable findings. No merge authorization has been given.

## Context and evidence

Baseline: commit `40554d983acff417ca641f681af1fd47b5b7d15f`, branch `feat/apps-between-channels`, clean initial checkout. `Audio/AudioService.cs` owns an MTA worker queue, enumerates sessions on configured outputs, and publishes immutable `AudioState` snapshots. `UI/MixerWindow.cs` builds Settings and shows mixer sessions grouped by their actual endpoint. `App.xaml.cs` wires UI events to the audio worker. `Core/Models.cs` currently lacks typed session process IDs. `Platform/Preferences.cs` saves channel-to-device bindings, not per-app routing. Claude Haiku 5.5 was consulted at the user's request and recommended native selectors, flat running rows, and a collapsed inactive section. Its reset/removal suggestion is outside the requested three-channel feature, so no ambiguous `Not assigned` action will be introduced. A newly picked executable has an unassigned placeholder until the user selects a channel.

README explicitly says routing is absent. Issue #2 is future auto-classification and is not this manual routing task. No matching open PR exists. Keep auto-classification outside scope.

Verified baseline checks: pinned SDK 10.0.401 found at an existing task-local tool path, gesture/keyboard suite passes, locked main build succeeds with zero warnings/errors, read-only AudioProbe sees all four configured outputs. Ordinary PATH lacks the pinned SDK. Use the discovered SDK without changing global.json. Graphify structural extraction of affected C# files produced 229 nodes and 474 edges and traced MixerWindow through App to AudioService.

Prior art: inspected upstream EarTrumpet's `IAudioPolicyConfigFactoryVariantFor21H2`, downlevel variant, activation helper, and WindowsAudio/Internal/AudioPolicyConfigService. Its per-process routing uses `Windows.Media.Internal.AudioPolicyConfig` with build-specific interface GUIDs and an MMDEVAPI render interface path. This is an undocumented Windows interface. Inspect exact signatures and verify them on this machine before relying on them. Do not copy unrelated application code or add an external routing utility. Preserve source attribution in a short interop comment. Relevant primary sources:

- https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IAudioPolicyConfigFactoryVariantFor21H2.cs
- https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IAudioPolicyConfigFactoryVariantForDownlevel.cs
- https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/DataModel/WindowsAudio/Internal/AudioPolicyConfigService.cs

Research stopping rule: verified ABI, resource ownership, role semantics, and a successful isolated native round trip are sufficient. Broader routing engines or audio capture are unnecessary. A read-only child is checking the API and remaining uncertainty.

## Technical / Coding

Control flow sketch, using Show Me's smallest useful representation:

```text
Settings / App channels / active or configured inactive app / channel selector
  MixerWindow.AppChannelChanged(executablePath, channel)
    App saves Preferences.AppChannels before activating the change
      AudioService.ConfigureAppChannels clones assignments onto its worker
        validate supported live sessions and active bound destinations
        AudioRouting sets Windows per-app render preferences
        session/device notifications reconcile future launches
          Settings and Mixer show actual endpoint after migration
```

### Interfaces and responsibilities

- Modify `Core/Models.cs`: add process ID, process creation identity, single-process eligibility, and system-session identity to SessionLevel with backwards-compatible optional defaults. Keep deterministic route destination and identity eligibility in a small pure helper used by the worker/UI. Do not parse PID out of display text.
- Modify `Platform/Preferences.cs`: add `Dictionary<string,string> AppChannels` with case-insensitive full executable-path identity. Existing files default to empty. Validate/sanitize this property separately so malformed app settings do not discard existing keyboard/audio settings. Only Game/Chat/Media values and absolute executable paths survive load. Save before activation; a save failure must restore the prior in-memory mapping and leave routing unchanged. Do not auto-inventory other applications. Only explicitly assigned paths are persisted.
- Proposed `Audio/AudioRouting.cs`: one disposable native interop owner created lazily on the audio worker, released there. Activate the current or downlevel factory using the inspected build threshold, call raw ABI vtable slots 25/26 (the old IInspectable marshaler is unsupported on .NET 10), and manage all HSTRING and COM resources. Only Get/SetPersistedDefaultAudioEndpoint for render flow. Never call the global clear method. Read, set, and verify Console (0), Multimedia (1), and Communications (2) separately. Snapshot every original value, including a null/absent override, before any write. Do not turn failed reads into absence. On failure restore changed roles and verify restoration, surfacing rollback failure separately. Do not expose private endpoint IDs in user-facing errors. Keep the role transaction as a small internal callback-based helper so deterministic tests can exercise partial failure without mutating real preferences.
- Proposed `Audio/SessionCatalog.cs`: enumerate raw Core Audio session controls using standard COM interfaces, reusing NAudio's public IAudioSessionControl2. NAudio's convenience GetProcessID drops AUDCLNT_S_NO_SINGLE_PROCESS. Preserve the native HRESULT and expose single-process identity to the worker. Define only the standard COM members needed for enumeration. Do not use reflection into private NAudio fields.
- Modify `Audio/AudioService.cs`: enumerate sessions across all active render endpoints while retaining existing channel bindings for endpoint controls. `ConfigureAppChannels(IReadOnlyDictionary<string,string>)` clones saved assignments onto the existing worker. Capture PID plus process creation time at discovery. Immediately before writes, read the live native GetProcessId status and confirm S_OK, unchanged PID and creation time, a non-exited process handle, and an unexpired session. Reject multi-process or unverifiable identities and system sessions. Resolve the destination from current channel bindings at execution time. An inactive destination defers a saved assignment until it returns. Track each attempt by process creation identity, assignment revision, and endpoint so level notifications and migration rebuilds cannot produce a routing loop. A manual change can retry a failed assignment. New sessions/processes and changed destination bindings reconcile saved assignments. Publish typed process/system metadata and visible routing errors. Preserve existing levels and mute behavior.
- Proposed `UI/AppRoutingView.cs`: cohesive Settings section with native ComboBoxes, app label/icon, observed channel/output, `Add app...`, and a collapsed `Not running (n)` Expander. Group sessions by case-insensitive executable path. Exclude system sessions and show unsupported identity as unavailable. Use existing AppIcons fallback for inactive apps. Saved choice and observed output are separate: `Playing on Chat`, `Not running. Applies when it plays audio.`, or `Media output unavailable. Applies when it returns.`. Inactive/unavailable channels can still be saved. Never report a saved choice as observed success. Add/remove rows only on identity-set changes and update controls under a render guard, preserving focus and expansion state on level-only updates. The executable picker is initialized to the actual window HWND. Newly picked paths stay local to the view until a channel is chosen, then become durable assignments. No drag machinery or app auto-classification.
- Modify `UI/MixerWindow.cs`: include the section in Settings, update it from snapshots and saved assignments, forward its assignment event and file-picker request. Modify `App.xaml.cs` to subscribe once when creating MixerWindow, save through the same Preferences instance used by keyboard/device settings, and pass assignments into AudioService at startup and after a successful save.
- Update `README.md` and `CONTRIBUTING.md` with Settings flow, active-app and restart limits, Windows-owned persistence, and the exact opt-in probe command.
- Tests: add focused deterministic routing cases to the existing hardware-free test target, including stale/reused PID, ambiguous native status, unavailable channel, partial-write failure, restoration of distinct/absent preferences, and rollback failure reporting. Extend `Tests/AudioProbe` with an explicit routing mode and a dedicated owned player process using a Default-output renderer (such as looping silent PlaySound, subject to feasibility verification), not an explicit-device WasapiOut. Observe active session migration on Game -> Chat -> Media without explicitly opening those devices. Restart the same owned executable with a new PID and verify Media is still used without another route write. Keep an owned process alive for preference restoration and independent readback before shutdown. Include an initial session on the unbound default output when available, and confirm it appears as a routing candidate. Add an isolated WinUI test entry point following OverlayProbe if needed to verify the real Settings interaction without single-instance/startup side effects.

Do not split cohesive existing AudioService or MixerWindow solely for line count. Keep new UI and native interop responsibilities separate, with no dependency injection framework, extra package, drag-and-drop machinery, or application routing database.

## Work items

### T1: Freeze plan and create issue

Depends on the user outcome and acceptance above and primary-source API research. User clarified inactive apps are required and asked for Claude design consultation; both are incorporated. Independently review this complete plan, address actionable findings, then create and read back a GitHub issue containing the durable plan and acceptance. Review cap is three attempts, carried across resumes. Round 1 found four issues: discovery on unbound endpoints, concrete process identity, explicit role transaction, and valid migration/persistence proof. All are incorporated above. Round 2 was canceled when user scope expanded, and still counts. A native ABI diagnostic activated the factory on Windows build 26200. A non-audio process returned E_INVALIDARG, which upstream SoundSwitch names PROCESS_NO_AUDIO; after opening its own silent default-output playback, all three preference reads succeeded with null handles. Do not interpret failed reads as missing overrides. The owned routing probe must establish null restoration before production acceptance. If a native API feasibility check fails, stop dependent implementation and investigate the exact ABI error rather than reducing acceptance.

### T2: Implement native routing and Settings integration

Depends on T1. Implement typed session identity, validated Preferences.AppChannels, and the smallest native wrapper, then connect worker reconciliation, Settings section, and App event. Add deterministic regression cases for three allowed channels, unavailable/missing targets, expired/system sessions, duplicate app/process identity, malformed/legacy settings, case-insensitive path matching, failed-save rollback, and per-process attempt deduplication. Preserve all old constructors and mixer behavior. Rebuild with locked dependencies. The result is acceptable when an inactive app can be explicitly assigned without launching it, running apps receive the assignment, and a future supported session receives it without further user action.

### T3: Verify requested behavior

Depends on T2. From repository root using the pinned SDK:

- Run `dotnet run --project Tests/GestureTests.csproj -c Release`, including new routing rule checks. Expected: all old and new assertions pass.
- Run `dotnet run --project Tests/UpdateTests/UpdateTests.csproj -c Release`. Expected: unrelated updater behavior preserved.
- Run `dotnet build Mix.csproj -c Release -p:RestoreLockedMode=true`. Expected: successful build without new warnings.
- Run the existing read-only AudioProbe. Expected: current bindings remain present.
- Run the new opt-in routing probe against only its own playback process. Expected: Game -> Chat -> Media moves actual sessions, all intended render roles read back correctly, original routing restored, endpoint volumes/mute and system defaults unchanged. Keep device IDs out of shared evidence. Failure blocks runtime acceptance.
- Run the isolated WinUI Settings check. Expected: actual selector/menu reachable, choices match availability, one user action emits the intended app/channel request, observed output follows snapshots, disabled/empty behavior correct. Build-only evidence cannot replace this check.
- Verify offline assignment in an isolated settings file before starting the owned player. Load that preference into a fresh AudioService and observe its first session being routed to the selected channel. Stop and restart the same owned player with a different PID and verify the assignment is retained. Verify the inactive Expander and Add-app-to-selector path with the real Settings controls without writing the user's settings file. Retain the original native preferences and restore them with a live owned process before shutdown.
- Re-run CI checks required by the repository when publishing. Inspect the integrated diff and obtain an independent fresh-context review in an isolated checkout. Fix actionable findings and verify affected checks again.

Retain exact commands, exit status, tested SHA, native/UI evidence, and limitations in the issue/PR. Native probe changes are reversible only if finally restores prior preferences. If restoration fails, report the exact isolated test-app scope and stop.

### T4: Publish and verify readiness

Depends on T3. Apply Create PR preparation/conventions/evidence/publication references, commit only task files with configured identity, push normally, create a conventional `feat:` PR against the repository default branch with `Closes #issue`, and read back URL and head. Link it to this T3 thread immediately.

Before maintenance/watch registration, inspect all relevant ownership pages including subagents and exact PR links. No competing writer may remain. Register the main-thread native watch, verify it, and process catch-up feedback through a bounded read-only child. Main owns fixes and pushes. Fresh provider reads must establish every R6 condition at the same head/base. Pending CI or reviews means WAITING, not complete. Remove and verify the watch and end/cancel triage before handing back MERGE READY. Never merge, force-push, enable auto-merge, or bypass checks.

## Coverage and risks

R1/R2 -> T2/T3 -> real Settings interaction and native session migration. R3/R4 -> T2/T3 -> deterministic rejection cases, readback/restoration, unchanged endpoint state. R5 -> T3 -> retained native/UI probes. R6 -> T4 -> fresh provider state and verified watch cleanup.

Windows may accept a saved preference without migrating an app that explicitly owns its output. Show the observed endpoint and a playback restart/Default-output hint, rather than falsely reporting a move. If the API changes on another build, fail visibly without writing global preferences. A closed/reused process or session must be rejected before native writes. Multiple processes of one executable can share Windows routing identity, so do not promise independent routing of browser tabs or sessions. Document Windows app-level behavior.

## Skill handoff

Implementation evidence (2026-10-23): planning review round 3 passed after round 2 was canceled for the user's inactive-app requirement. Claude design consultation completed. Issue #25 retains this plan. Deterministic preference/identity/transaction checks, live Default-output AudioGraph migration through all three outputs, new-PID persistence, offline preference loading, independent unchanged global defaults and endpoint levels, verified null-preference restoration, and real WinUI active/inactive/add-without-assignment controls passed. A WinUI fixture screenshot is saved at `docs/images/app-channels.png`. Windows build 26200 was exercised; other Windows builds and the system file-picker dialog remain untested. Independent implementation review and provider PR readiness are still pending.

Skill discovery: COMPLETE for the engineering discipline. Searches `npx --yes skills find windows dotnet audio` and synonym `npx --yes skills find winui` returned no suitable audio-specific skill and established WinUI candidates. The unrelated angular-security result was rejected. OpenAI's `openai/skills@winui-app` has 3.2K listed installs but installation is not needed because the existing project and native probes provide the relevant workflow. No skill installation requested or performed.

Loaded: Execute Task End to End for issue-to-readiness responsibility, Create Plan and its relevant references for plan quality, Ponytail and Keep It Simple for minimal complete implementation, Show Me for the control-flow sketch, Agent Swarm for bounded read-only research/review, Graphify for affected relationships. Use Create PR at publication and PR Babysit rules with the end-to-end T3 mapping for main-owned monitoring. Main implements and integrates. Fresh-context reviewers stay read-only. Benchmark catalog checked 2026-10-23 from cached Artificial Analysis data fetched 2026-10-22. Quota telemetry unavailable. Luna high is selected for bounded API research, a stronger supported Codex route for native interop review.
