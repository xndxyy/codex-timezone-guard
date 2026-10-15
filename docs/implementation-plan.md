# Codex Timezone Guard Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the implementation vertical slice; execute packaging and publication with verification-before-completion. Follow the checked steps without repeated human approval.

**Goal:** Ship the approved A-isolated Windows Codex-only timezone launcher, safe target-aware detection, Chinese UI, tested single-file EXE and honestly scoped open-source prerelease.

**Architecture:** Core logic is independent of Windows Forms. The desktop project orchestrates cancellable detection and safe launch; the test executable uses real core logic with injected transport and process fixtures. System proxies are read-only and all external requests are fixed HTTPS, anonymous and bounded.

**Tech Stack:** .NET 10 / C#, Windows Forms, WinHTTP and process Win32 interfaces only where necessary; no third-party application packages or bundled proxy engine. SDK 10.0.400 is available locally. All development caches live in .cache/ and outputs in artifacts/ or outputs/.

## File map

- `src/CodexTimezoneGuard.Core/`: timezones, settings, network/egress safety, Windows proxy resolution, desktop discovery and launch.
- `src/CodexTimezoneGuard/`: WinForms application, Chinese workflow, diagnostics, preview/self-test command modes.
- `tests/CodexTimezoneGuard.Tests/`: no-network deterministic console test runner with named checks, nonzero failure exit and optional JSON report.
- `tests/EnvironmentProbe/`: standalone child executable that reports its own TZ/date/PID to a specified output; never acts as a Codex candidate in production.
- `scripts/`: project-local environment, verification and single-file publish scripts; no OS scheduling or global settings changes.
- `docs/`: design, sources, privacy/security/compatibility, user acceptance and evidence.
- `.github/workflows/`: read-only-permission Windows build/test workflow with dependency-free reproducible build commands; release upload remains explicit.

## Task 1 — Approve/copy boundary and prepare project (controller)

- [x] Copy documents only into the empty user-selected directory; initialize `codex/implementation` without touching the original repository.
- [x] Record user approval and EC-002; update the workspace location.
- [x] Fetch current .NET documentation with library + two concept-specific docs queries. Keep documentation separate from runtime evidence.
- [ ] Commit the reviewed execution plan. Verify `git rev-parse --show-toplevel` is the new directory.

## Task 2 — Test-first complete application vertical slice (one implementation worker)

- [ ] Create SDK project definitions, the independent test runner and failing behavior contracts before production functions. Each feature must have observed RED output for the missing behavior, not just an absent-project compilation failure.
- [ ] Implement timezones/configuration after these representative assertions fail:

```csharp
Assert.Equal("America/Los_Angeles", Timezones.Validate("America/Los_Angeles"));
Assert.Throws(() => Timezones.Validate("America/Not_A_Zone"));
Assert.Throws(() => PublicIp.Parse("127.0.0.1"));
```

The final public signatures may be renamed coherently by the implementer; tests and UI must use the same signatures. Include London summer/winter offsets, invalid controls, IPv4/IPv6 special ranges, corrupt settings and atomic replacement. Settings contain no IP or credentials.

- [ ] Write network tests showing a target trace IP B is explicitly looked up even if a generic service would see A. Test 403/404/429, duplicate trace fields, echoed-IP mismatch, malformed/oversized response, cancellation, two observations disagreeing and explicit PAC failure without DIRECT fallback. Transport injection is limited to tests; production URLs remain allowlisted.
- [ ] Implement bounded anonymous HTTPS, proxy/PAC resolution and two fresh observations plus auxiliary endpoint. Auto results are suggestions only; conflicts/insufficient evidence return manual state. No stale async completion may replace a newer request.
- [ ] Write process tests for child-only environment replacement, case-insensitive removal of inherited ELECTRON_RUN_AS_NODE, exact-path running guard and CLI rejection. Run EnvironmentProbe with a selected TZ and without it; parent/global settings must remain unchanged. No test may start/stop real Codex automatically.
- [ ] Implement desktop discovery using package/desktop identity rather than filename. Store launch uses suspended process creation and package/AUMID verification, failing safely without ACL/packaged-debug changes. Recheck running identity immediately before starting; unknown permission is not evidence of absence.
- [ ] Build the Chinese UI: first-network disclosure, automatic/manual mode, target timezone search/preview, discovery/manual target, detect/cancel, launch, masked diagnostics and reset. Refresh automatic evidence on launch, prevent duplicate launch, keep manual usable after failure. Do not label process-created as runtime-verified.
- [ ] Include `--self-test <report.json>` for packaged local/offline validation and `--render-preview <image.png>` for actual WinForms layout rendering without launching Codex or making external requests. Optional explicit `--diagnose <report.json>` may run real anonymous probes with masked evidence only.
- [ ] Run full test runner and app build with zero errors; save red/green results and list limitations. Report exact commands/file paths to controller. Do not push/release, modify controller docs or commit another worker's files.

Run from the project root with project-local environment:

```powershell
dotnet run --project tests/CodexTimezoneGuard.Tests -c Release
dotnet build src/CodexTimezoneGuard -c Release
```

Expected: named passing checks, a nonzero count, zero failures and build exit 0. Individual behavior RED evidence remains in outputs/tdd/ rather than being overwritten.

## Task 3 — Independent two-stage review

- [ ] Fresh spec review: compare goal/design to actual files and tests. Require precise findings and do not accept fixture evidence as real client verification.
- [ ] Resolve all material spec gaps; rerun relevant RED/GREEN regressions, then obtain spec approval.
- [ ] Fresh quality/security review only after spec review passes: proxy failure paths, process race/identity, credentials, cross-application isolation, cancellation and settings persistence.
- [ ] Resolve material findings and rerun all tests. Preserve all review reports/limitations.

## Task 4 — Packaging, visual inspection and safe network check (controller)

- [ ] Publish with self-contained `win-x64`, PublishSingleFile and IncludeNativeLibrariesForSelfExtract enabled; no trimming. Redirect DOTNET/NuGet/TEMP/bundle extraction to project-local directories in development.
- [ ] Run the packaged EXE self-test with an explicit report path and inspect JSON, not just process exit. Render the actual form to PNG and inspect text/clipping; this is rendering evidence, not manual E2E.
- [ ] Run the explicit diagnostic command once; save only masked operational evidence. A target returning 403 is a real negative result, not success. No alternate generic IP is silently substituted.
- [ ] Record Windows build, binary size and SHA-256. Label ordinary/Desktop/Store/real runtime checks accurately. Do not close the active Codex session for acceptance.

## Task 5 — Public documentation, CI and release

- [ ] Write Chinese-first README, English summary, PRIVACY, SECURITY, MIT/third-party notices, source build commands and compatibility/manual acceptance instructions.
- [ ] Add Windows CI calling the same verification/build scripts; workflow uses contents:read and no user input shell interpolation or secrets.
- [ ] Scan all staged files for credentials, personal paths, raw IPs and SNN files. Documentation intentionally names the project workspace but contains no account token/config data.
- [ ] Verify authenticated GitHub owner and nonexistence/collision of `xndxyy/codex-timezone-guard`; do not overwrite an existing unrelated repository.
- [ ] Commit reviewed source to `codex/implementation`, make the public default main branch from the reviewed commit and create a labeled prerelease because real Codex runtime verification requires a safe user restart.
- [ ] Upload EXE, ZIP if useful, SHA256SUMS and verification report. Compare the downloaded release asset hash to local artifact, keeping any downloads in artifacts/verification/.
- [ ] Update goal/plan/status/error_correction with actual counts, limitations, commit/tag/URLs. Report what shipped and the remaining user-controlled real-client check.

## Self-review

Coverage: all nine design sections map to Tasks 2–5; supported proxy types, privacy consent, child inheritance and evidence separation are explicit. The original source directory is preserved, not recursively moved or deleted. All user approval gates are satisfied; no further approval is needed for reversible implementation or the requested public prerelease. Real-client restart remains user-controlled.
