# Win Mix open-source readiness plan

Implementation was authorized on 2026-09-21. The owner subsequently selected GPL v3.0 only, superseding the MIT proposal below. The sections below retain the original planning baseline and acceptance criteria. Current execution evidence and remaining gates are recorded in [VERIFICATION.md](../../VERIFICATION.md#open-source-readiness-implementation-2026-09-21).

## Objective and delivery boundary

Make Win Mix understandable, buildable and maintainable by someone outside the original development environment. The requested deliverable is a researched plan submitted as a pull request. This PR changes documentation only. It does not apply a license, change repository settings, modify workflows, install software, or publish another release.

The user requested inspiration from comparable tools and their other public projects, and explicitly waived grilling. Recommendations below are implementation proposals, not new owner commitments.

## Definition of Done

For this planning PR: a cited comparison, an evidence-based gap inventory, sequenced file-level tasks, explicit acceptance gates, independent review, and a provider-verified PR against `main`.

For the later implementation: an outside contributor can identify reuse terms, find support and private reporting instructions, reproduce the documented build, and submit a change that receives read-only CI checks. A clean Windows machine can install and run the published application without development tools. Outstanding manual checks must remain visible until actually passed. A merged planning PR alone does not establish product readiness.

## Context and evidence

Baseline: `6702036067b36ef3c2f9854a76eba73a99e979fc`, inspected 2026-09-21. The repository is public and GitHub reports no recognized license. There is no root LICENSE, CONTRIBUTING.md, SECURITY.md or PR validation workflow. GitHub private vulnerability reporting is currently disabled, verified through the repository API. There are already dependency notices in `Licenses/`, a useful README, deterministic tests, a locked dependency graph, an installer and a tag-triggered release workflow.

- [README](../../README.md) documents Windows 10 2004+ x64, Sonar-oriented controls, build commands and limitations.
- [Project](../../Mix.csproj) targets .NET 10 and WinUI, copies third-party notices and uses a locked restore during publication.
- [SDK pin](../../global.json) requires exactly 10.0.401 with roll-forward disabled.
- [Build script](../../build.ps1) runs gesture tests, publishes self-contained output, verifies resources/version and creates the installer/checksum.
- [Release workflow](../../.github/workflows/release.yml) builds on Windows, pins action revisions and separates release write permission into the publishing job.
- [Verification](../../VERIFICATION.md) records successful development-machine and release checks, explicitly leaving clean-machine, accessibility, monitor/DPI and physical-device scenarios incomplete.
- [Research comparison](../research/open-source-readiness.md) supplies external evidence and the owner's public-project examples.

The v1.0.1 release and its installer/checksum assets were verified through GitHub metadata during planning. Historical runtime claims above come from VERIFICATION.md and were not rerun. The release version/tag guard check passed locally. This shell has .NET SDKs 7 and 9, not the pinned SDK, so no fresh application build is claimed.

## Requirements and scope

| ID | Required outcome | Scope |
| --- | --- | --- |
| R1 | Visitors can determine permitted reuse and third-party obligations | Project license, dependency notices, asset provenance |
| R2 | Users understand purpose, installation, controls and limitations | README, genuine screenshots, troubleshooting |
| R3 | Contributors reproduce checks without private machine paths | CONTRIBUTING.md, existing build/test entry points |
| R4 | Untrusted pull requests receive useful checks without publishing authority | Separate PR CI, no release behavior changes |
| R5 | Bug reports are actionable and sensitive reports have a verified private route | Templates and reporting guidance |
| R6 | Public compatibility claims match retained evidence | Clean-machine and interactive acceptance record |

No audio architecture rewrite, channel expansion, updater, telemetry, installer migration, package-manager submission, paid signing, website, localization framework or additional runtime dependency is needed for these outcomes. Do not port EarTrumpet or SoundSwitch features just because they exist.

## Decisions and assumptions

| Decision | Status | Choice and reason | Revisit trigger |
| --- | --- | --- | --- |
| Delivery | Decided | Research and plan PR, as requested through planning/research skills | Explicit implementation request |
| Licensing | Proposed | Prefer plain MIT, matching both owned projects in the research, subject to verified ownership and owner approval. Do not infer a license grant from public visibility | Owner approval before adding LICENSE |
| Contributor tooling | Proposed | Reuse PowerShell, dotnet and current tests | A demonstrated unmet check |
| CI shape | Proposed | One read-only Windows validation job on PRs and pushes to main | Measured duplication or cost justifies reuse |
| Support | Proposed | GitHub issues for ordinary bugs, verified private channel for vulnerabilities | Maintainer chooses another supported channel |
| Compatibility | Decided by existing project | Keep current Windows/x64 and audio-routing boundaries | New platform explicitly requested and tested |

The plan can be reviewed and merged with proposed licensing/reporting choices. Implementing those choices is gated on owner authority and verified capability. They do not block the documentation deliverable.

## Technical / Coding

No application API, settings schema or audio behavior changes are proposed. Keep `Mix.Native` paths and registry keys stable. `Preferences` persists endpoint IDs and selection, while `App` writes exception details to a local error file. Support instructions must tell users to redact device IDs and personal paths before posting files.

Proposed file map:

```text
LICENSE                         owner-approved project terms
README.md                       installation, real visuals, controls, support links
CONTRIBUTING.md                  prerequisites, build/check commands, change boundaries
SECURITY.md                     verified private report route and supported-version policy
.github/ISSUE_TEMPLATE/          small bug and feature templates
.github/pull_request_template.md summary and honest validation
.github/workflows/ci.yml         PR and main validation, contents: read
Mix.csproj                      package the approved root license with existing notices
VERIFICATION.md                 dated manual and clean-machine evidence
```

The existing release workflow remains the only publisher:

```text
pull request -> Windows checks -> test/build result
version tag  -> existing build.ps1 -> existing release job -> installer + checksum
```

CI should use the current checkout/setup-dotnet pins and `global-json-file: global.json`, then run `./Tests/ReleaseChecks.ps1`, `dotnet run --project Tests/GestureTests.csproj -c Release` and `dotnet publish Mix.csproj -c Release -o publish -p:RestoreLockedMode=true`. These commands already exist in project scripts or docs. Explicitly propagate native-command failures in PowerShell. Do not use `pull_request_target`, secrets, release permissions or hardware-changing probes for PR validation. Test publication resource presence using the existing build script's resource list. Full installer compilation stays in release validation unless evidence shows a PR packaging gap worth the added setup.

## Execution tasks

### T1: establish reuse terms and attribution (R1)

Dependency: owner authorization to implement and adopt a specific license. Read the cited own-project examples, git authorship, `Licenses/`, `Assets/icon-prompt.txt` and dependency lockfile first.

- [ ] Inventory project-owned code/assets versus bundled third-party material. Preserve existing notices and identify any missing redistribution terms before selecting text.
- [ ] Obtain approval for the proposed license and copyright attribution. Add the exact approved text to root LICENSE. A public repository is not evidence of a grant.
- [ ] Add a README license link and a `Content` entry in Mix.csproj to copy LICENSE alongside the existing notices into output and published files. Keep vendor notices intact.
- [ ] Run the documented publication command. Inspect `publish/LICENSE` and `publish/Licenses/`, then inspect an installer built through build.ps1.

Done when source and distributed artifacts contain approved terms and required notices. Stop on disputed provenance or incompatible terms. Keep the change in a reviewable branch until resolved. Do not retroactively change hosted release assets as part of this task.

### T2: make the first-use and contribution paths self-contained (R2, R3)

Dependency: T1 for final license wording. Documentation drafting can proceed in parallel with T1's ownership review.

- [ ] Restructure README around purpose, download, requirements, controls, limitations, troubleshooting and development links. Preserve the unsigned-build warning and distinction between Windows endpoint control and Sonar's internal mixer.
- [ ] Capture a genuine mixer/quick-controls screenshot after permission to run the app. Crop unrelated desktop content, inspect for identifying information and add descriptive alt text. Do not substitute generated artwork for runtime proof.
- [ ] Add CONTRIBUTING.md with exact SDK pin, Windows x64 prerequisites, quick deterministic tests, locked publication and full installer build. Inno Setup is needed for installer creation, not the pure gesture test. Use the existing configurable compiler path, never a maintainer-specific path.
- [ ] Document `Core/`, `Audio/`, `Platform/` and `UI/` responsibilities from source. Explain how to validate gesture changes versus hardware-dependent audio changes.
- [ ] Document local settings/error-file locations and redaction requirements. Explain that the read-only audio probe is distinct from its explicit exercise mode, which changes live levels temporarily.
- [ ] Have a fresh Windows checkout follow the commands literally. Record prerequisites, command exits and produced files. Fix instructions when the documented path fails.

Done when a contributor needs no private files or prior conversation to run checks and locate the affected module. If prerequisites cannot be installed, record an environment blocker rather than relaxing the SDK pin or claiming a successful build.

### T3: add contribution validation without release authority (R4)

Dependency: T2's documented commands. This is a workflow/security boundary, so require distinct fresh-context planning and post-change reviews before acceptance.

- [ ] Add the proposed ci.yml with `pull_request` and `push` to main, Windows runner, `contents: read`, pinned actions and `persist-credentials: false`. Reuse the pinned SDK and existing commands described above.
- [ ] Keep the release workflow and tag behavior unchanged. No live audio exercise, installer launch, startup registration or hardware test belongs in hosted CI.
- [ ] Run the commands locally where prerequisites exist. Validate workflow syntax and inspect the effective event/permission paths.
- [ ] Open a test PR and retain its run URL/head SHA. A harmless deliberate failing assertion on a disposable test branch must fail the job, then its removal must pass. Never leave the deliberate failure in the final branch.
- [ ] Consider making that check required only after its name and fork-PR behavior are proven. Changing branch protection is a separate owner-authorized settings action.

Done when a PR produces green build/test evidence at its head and failure propagation is demonstrated, with no secret/write-token dependency. If hosted execution fails, investigate logs and fix the workflow before calling it ready. Revert only the new workflow if needed, preserving release publishing.

### T4: make reports useful and safe (R5)

Dependency: verified reporting destination and maintainer-supported version scope. Private vulnerability reporting is disabled at this baseline, so enabling it requires an explicit owner-authorized settings action, or the owner must supply another private route. No response-time or long-term support promise is inferred.

- [ ] Add a small bug template asking for app/Windows version, device setup, reproduction, expected/actual behavior and sanitized diagnostics. Add a feature template asking for the problem and desired outcome, without committing to implementation.
- [ ] Add a PR template with summary and validation, including explicit manual-test limitations. Do not pre-check acceptance boxes.
- [ ] Verify whether GitHub private vulnerability reporting is enabled. If enabled, test that the signed-in reporting entry is available without submitting a report. Otherwise obtain an owner-approved private contact before publishing SECURITY.md instructions. Do not direct vulnerabilities to public issues or invent a mailbox.
- [ ] Cross-link reporting instructions from README. Preview templates in GitHub and verify links. Avoid governance committees, CLA automation and bots without an actual need.

Done when ordinary reports capture reproduction details and the private route exists. Report-channel settings changes require explicit authority and the applicable independent reviews. A missing private route blocks SECURITY.md publication, not unrelated contributor documentation.

### T5: prove the advertised download journey (R6)

Dependency: authorization for interactive app/installer execution on a disposable Windows environment. This task can start against existing v1.0.1 independently of T1-T4. Feed its findings into T2 and VERIFICATION.md. Repeat the affected packaging checks after T1 or any later installer change.

- [ ] Download the exact intended release installer and sidecar. Verify their match before installation and retain release tag and hash in VERIFICATION.md.
- [ ] On Windows without developer runtimes, install as a standard user, launch from the Start menu, inspect bundled-runtime loading, use tray/mixer/quick controls, and uninstall. Record OS and app versions. Do not extrapolate one Windows version to the whole support range.
- [ ] Exercise the already-listed manual gaps: keyboard/mute, theme, DPI/monitors, actual unplug/reconnect, session disappearance, sign-in startup and installer Retry/Cancel. Use test audio endpoints and record which cases could not be run.
- [ ] Verify upgrade preserves device preferences and startup choices, while uninstall removes only app-owned data. Retain evidence without exposing raw user configuration.
- [ ] Update VERIFICATION.md with dated pass/fail/blocked results and link only real screenshots or recordings. Leave incomplete rows explicitly incomplete.

Done when required clean-machine and manual scenarios pass. If no disposable environment is available, keep acceptance partial and preserve the narrow claims already supported. Code signing and package-manager distribution are later owner decisions, not prerequisites to honest unsigned distribution.

## Acceptance and final integration

| Requirement | Task | Evidence needed | Current state |
| --- | --- | --- | --- |
| R1 | T1 | Approved license in source, publish directory and installer | Missing project license |
| R2 | T2 | Working download links, genuine visuals, accurate limitations | Text onboarding exists |
| R3 | T2 | Fresh-checkout commands pass | Existing scripts, fresh run pending |
| R4 | T3 | Successful head-SHA PR run and demonstrated failure propagation | Tag release CI only |
| R5 | T4 | Templates preview correctly, private route verified | Proposed |
| R6 | T5 | Dated clean-machine and interactive results | Partial historical evidence |

Final implementation review must trace every row to retained evidence. A green build alone cannot close runtime rows. Keep source/asset licensing blockers separate from ordinary polish. Do not merge, tag, publish, change security settings or spend on signing merely because this plan was accepted.

## Risks, recovery and deferred decisions

- License/asset ownership: owner resolves before T1 publication. No copied competitor code or assets is needed.
- Hostile PR code: read-only CI with no credentials or release path. Stop if a workflow requires privileged execution of PR content.
- Hardware and installer side effects: run T5 only in an authorized disposable environment. Restore test levels and stop on changes outside app-owned state.
- Documentation drift: link SDK/version facts to their existing single sources and verify commands on the implementation head.
- SDK availability: the current shell cannot prove a .NET 10 build. Use a provisioned runner or an explicitly prepared development environment in implementation.
- Signing, Store/winget distribution, localization and broader audio support: deferred until demand and separate scope justify them.

## Execution handoff

Use the installed research and create-plan skills when revising evidence or scope, Ponytail/keep-it-simple for implementation, independent reviews for T3 and security-setting changes, and create-pr after relevant checks pass. No new skill installation is required.

Skill discovery: COMPLETE for readiness documentation and CI review. Searches `npx skills find "open source documentation"` and `npx skills find "github actions security"` identified [Trail of Bits open-sourcing](https://github.com/trailofbits/skills) and [Sentry workflow review](https://github.com/getsentry/skills). Both were evaluated as relevant but deferred because existing research and review capabilities cover the plan. Discovery did not install or invoke either candidate.
