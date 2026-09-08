# AJCC-X v0.0.2 — FirstLight

## Purpose

FirstLight is the first real cross-platform desktop shell on top of the validated `AJCC.Core` Foundation. It is intentionally a new UI layer, not a mechanical WPF conversion.

The productive Windows/WPF AJCC remains untouched.

## Current checkpoint — 2026-09-03

FirstLight has grown substantially beyond the initial vertical slice documented chronologically below.

- validated runtime/test head: `1f71298c8fc0cb5e85f8d45994cb23a6c6fd1d5f`
- CI #522 / run `33739383122`: Restore, warning-free Release Build, 230 AJCC.Core tests and 3 headless AJCC.Desktop service tests successful on Ubuntu, macOS and Windows
- release hardening now includes exact PR-head checkout/build metadata, privacy-sanitized and 4 MiB-bounded startup diagnostics, atomic persistent Desktop JSON writes, a cross-platform Desktop-service test gate, current-user-only single-instance import IPC and explicit opt-in (default off) for feedback technical context plus the anonymized diagnostics ZIP
- the controlled productive-semantic reverse backfill is complete down to the earliest reliably documented boundary: versioned productive history through v0.1.52 plus the documented pre-import `AnonymousDiagnosticsExportPrivacyFix` state
- major productive workflows are represented across Core connection/bootstrap/polling, Core profiles/failover, downloads, uploads, search, servers, shares, Core settings/readback, Core-side directory browsing, Share-directory draft/descendant semantics, AJFSP/AJL, Incoming mapping, external VLC safety, diagnostic ZIP export and integrated feedback
- Core passwords remain transient and are deliberately not persisted; safe desktop/UI preferences are persisted separately
- the expensive live-load checks for Queue Priority Ranking, 0-source Queue rotation and ListOrder pause/resume are intentionally waived for FirstLight rather than manufacturing suitable network/load conditions

The sections below retain the chronological development record. Where an older section describes an earlier “current” or “not yet included” state, this checkpoint supersedes that status statement.

## Branch model

- base: `foundation/v0.0.1-foundation`
- development: `firstlight/v0.0.2-firstlight`
- `main` remains untouched until Martin explicitly decides otherwise
- PR #2 stays draft until Martin explicitly decides otherwise

## UI framework

FirstLight uses Avalonia 12.1.1 and targets .NET 10.

`AJCC.Desktop` references `AJCC.Core`; `AJCC.Core` does not reference Avalonia or another desktop framework.

## Current vertical slice

FirstLight currently provides:

- absolute Core endpoint input (`http` / `https`, optional port and base path)
- transient Core password entry without repository/config persistence
- connection validation through the Foundation transport
- runtime bootstrap through `CoreRuntimeBootstrapper`
- continuous `modified.xml` polling through `AjPollingService`
- dashboard data for Core identity/version, network size, credits, transfer speeds and list counts
- live download list
- selected-download pause/resume
- confirmed download cancellation
- selected-download rename
- selected-download Core-relative target-directory change
- live upload list
- live server list
- live search list and result list
- starting searches through the real `/function/search` endpoint
- taking a selected search result over as a download on the currently connected Core through AJFSP + `/function/processlink`
- cross-platform context menus for downloads and search results
- cross-platform clipboard copy actions through Avalonia

Search cancellation is deliberately not exposed. Although the historical endpoint inventory contains `/function/cancelsearch`, real Core behavior does not reliably abort a running search, so FirstLight must not present that as a working function.

## Download control compatibility

The productive WPF AJCC currently uses:

- pause: `/function/pausedownload` with historical uppercase query parameter `ID`
- resume: `/function/resumedownload` with lowercase query parameter `id`
- cancel: `/function/canceldownload` with lowercase query parameter `id`

FirstLight preserves those protocol conventions in the platform-neutral Core layer and covers them with Core regression tests.

Desktop action semantics match the productive client:

- status `18` = paused
- terminal states `14`, `15`, `17` are not actionable
- pause is allowed only for non-paused, non-terminal downloads
- resume is allowed only for paused, non-terminal downloads
- cancel is allowed for every non-terminal download
- rename and target-directory changes are allowed only for non-terminal downloads

The Core remains authoritative for the resulting state. FirstLight sends the action and reflects the later state transition through normal Core polling.

### Destructive action guard

`Download abbrechen` is not sent directly from a context-menu click. FirstLight first opens an owner-modal Avalonia confirmation window showing the selected filename. Only the explicit confirmation button invokes the Core cancel action. Closing the dialog or choosing `Zurück` does not call the Core.

This replaces the productive WPF confirmation dialog with an Avalonia implementation without introducing WPF dependencies.

## Download metadata compatibility

The current productive WPF source was re-read before implementing rename and target-directory changes.

Protocol conventions preserved in `AJCC.Core`:

- rename: `/function/renamedownload?id=...&name=...`
- target directory: `/function/settargetdir?id=...&dir=...`
- the target-directory query value preserves `/` and `\\` directory separators, matching the productive client's old-Core compatibility behavior
- `/xml/directory.xml` transport is available through `GetDirectoryXmlAsync` for a later Core-side directory browser

### Core-owned target path

The target path belongs to the **connected Core**, not to the computer on which the Avalonia GUI happens to run. FirstLight therefore does not open a Windows/macOS/Linux local folder picker and does not create local directories.

The target dialog accepts a relative path below the connected Core's `IncomingDirectory`:

- empty value means the Core's Incoming directory itself
- absolute Windows paths, absolute Unix paths, UNC-style roots and `.` / `..` traversal are rejected
- separators are normalized to the separator inferred from the connected Core's Incoming path
- problematic path characters are normalized through the migrated platform-neutral `CoreTargetPathSanitizer`
- when normalization changes the entered path, the dialog shows the normalized value and requires a second explicit `Übernehmen`

This is intentionally different from the productive Windows-only local filesystem preparation path. AJCC-X must not pretend that a local directory on the GUI machine is the Core's directory when the Core can run remotely or on another operating system.

The current slice does not assert that an arbitrary new directory already exists on the Core and does not attempt remote directory creation. The Core remains authoritative. A later Core-side directory browser can use `/xml/directory.xml` without weakening this boundary.

## Search-result download handoff

FirstLight mirrors the productive WPF workflow on the same connected Core:

1. validate filename, 32-character checksum and positive file size
2. build the plain AJFSP file link through `AjfspLinkBuilder`
3. resolve the existing `AjCoreCompatibilityProfile` from the connected Core version
4. submit the link through `/function/processlink`
5. interpret the Core response through `AjProcessLinkResult`
6. repeatedly request `modified.xml` with the current session and download filter until the matching hash appears or the short visibility timeout expires
7. select the matching download when it becomes visible

The complete runtime path therefore stays inside one Core instance:

`FirstLight search → FirstLight processlink → connected Core download list → download actions`

## Context menus

Context menus are implemented with Avalonia `ContextMenu` and the platform-neutral `ContextRequested` event rather than Windows-specific mouse handling.

### Downloads

- Pausieren
- Fortsetzen
- Download abbrechen…
- Umbenennen…
- Zielverzeichnis setzen…
- AJFSP-Link kopieren
- Dateiname kopieren
- Hash kopieren

The cancel item always passes through the confirmation dialog before the Core can be called. Rename uses a small owner-modal Avalonia text dialog. Target-directory editing uses a dedicated Avalonia dialog that exposes the Core/GUI filesystem distinction directly.

### Search results

- Als Download übernehmen
- AJFSP-Link kopieren
- Dateiname kopieren
- Größe kopieren
- Checksum kopieren

Clipboard access uses Avalonia `TopLevel.Clipboard`; no Windows clipboard API is referenced.

## Correction note — 2026-08-15

A temporary test diagnosis incorrectly treated a download created in the productive `Standard-Core` as if it should appear in FirstLight connected to the separate `AJ-Core1` instance. Those are independent Core processes with independent runtime state, so that expectation was invalid and did not demonstrate a FirstLight polling defect.

The speculative desktop logic that automatically forced a runtime snapshot when an active object ID was missing was therefore removed again. We do not keep architecture changes whose justification came from mixing two Core instances.

The sessionless `timestamp=0` initial runtime snapshot introduced during that investigation remains because it matches the current productive WPF startup semantics. Normal live polling uses the established Core session.

## Search protocol compatibility

The productive AJCC sends search requests as HTTP POST and encodes search terms with the legacy Java/phpGUI-compatible RFC3986-style behavior where spaces are `%20` rather than `+`.

That behavior is migrated into the platform-neutral `AppleJuiceCoreClient` and covered by Core tests. Transport still uses `CoreEndpoint`, so HTTPS and reverse-proxy base paths remain supported.

## Thread boundary

`AjPollingService` performs network polling away from the desktop UI. FirstLight applies parsed `ModifiedParseResult` instances to the bound `AjState` on Avalonia's UI dispatcher before state changes reach controls.

## AJCC visual language

FirstLight no longer uses the raw Avalonia Fluent appearance. Its application-level style layer reuses the productive AJCC visual language without porting WPF controls or templates:

- background `#15171C`
- primary panel `#20232B`
- secondary panel `#282C35`
- accent `#4DA3FF`
- text `#F3F6FA`
- muted text `#AEB7C2`
- input background `#111318`
- selection `#355A86`
- compact density
- explicit AJCC tab and list-row chrome
- compact Core connection toolbar
- bottom status bar
- online state badge
- locked connection settings while connected

The current visual direction was reviewed locally on Windows and accepted as a substantial improvement over the raw Fluent version.

## Windows live validation — 2026-08-15 / 2026-08-16

FirstLight was tested locally against the password-protected `AJ-Core1` at `http://127.0.0.1:8851/`.

Confirmed live:

- Avalonia application launch
- password-protected connection
- Core identity `AJ-Core1`
- Core version `0.31.149.113`
- network/runtime data
- server list
- continuously advancing Core timestamp through `modified.xml`
- live transfer values
- real searches including `linux` and `matrix`
- search-result rendering
- selected search result → AJFSP/processlink → download on the same connected `AJ-Core1`
- pause/resume transition on a real AJ-Core1 download
- download and search-result context-menu gestures
- Avalonia clipboard copy actions
- cancel confirmation guard: `Zurück` leaves the download untouched
- confirmed download cancellation against AJ-Core1
- resulting Core-owned state transition through normal polling
- current AJCC-style visual layer

The rename/target-directory metadata slice added after those checks is CI-validated across all three operating systems but is **not yet recorded as live-validated against AJ-Core1**. That validation must happen before the documentation claims real-Core success for those two actions.

## CI gate

The GitHub Actions matrix builds the complete solution on Windows, Linux and macOS. Every matrix job checks out the exact PR source head, performs a Release build, runs 230 Core regression tests and runs the headless Desktop-service persistence tests.

Validated heads:

- `ece7125c1029501046ce2deec96565c5a118c440` — first AJCC style pass, workflow `31890377932`, all three OSes green
- `0d3c0f54da8f01442417147522a99d40e3c6b6ab` — custom tabs/lists/status chrome, workflow `31890989270`, all three OSes green
- `1a3b03be3dbcd8d4a59758fde6249a360cdd2a21` — connection-state polish/edit locking, workflow `31891131595`, all three OSes green
- `c3c645f9fbd12d3194e6edba458088c698116e86` — download pause/resume slice, workflow `31891782144`, all three OSes green
- `28b4e45c3641120bc7e7f6138cc45a9669d8041a` — same-Core search-result download handoff, workflow `31893234914`, all three OSes green
- `24a62452b6fdf674348ab99e9238cef0c278b4b0` — cross-platform context menus + Avalonia clipboard, workflow `31931634467`, all three OSes green
- `d65d00538c247ffce659beebc01790960d7bacfd` — confirmed download cancellation + cancel transport regression test, workflow `31931907875`, all three OSes green
- `7a992a93d6d2a15e796bc6e48d440bd26bcac8e6` — portable rename + Core-relative target-directory slice and regression tests, workflow `31933020732`, all three OSes green
- `3f7b135a431aed30d6a40d1321670fb05ad31819` — release-hardening checkpoint with exact-head CI, atomic Desktop settings and 230 Core + 3 Desktop-service tests, workflow `33736965594`, all three OSes green
- `947373ab9f336ce4d9623db3cb3409c75ab30d4f` — single-instance import IPC restricted to the current OS user, CI #520 / workflow `33738389010`, all three OSes green
- `1f71298c8fc0cb5e85f8d45994cb23a6c6fd1d5f` — feedback technical context and anonymized diagnostics ZIP require explicit opt-in, default off, CI #522 / workflow `33739383122`, all three OSes green

## Current scope boundaries

The following items remain intentionally outside the current FirstLight checkpoint or require a separate explicit product decision:

- installer / packaging
- tray integration
- OS-level protocol/file-association registration
- light-theme/theme switching
- remote Core directory creation; FirstLight can browse Core directories but does not pretend that the GUI host can create arbitrary directories on a remote Core
- OS credential-store integration while Core-password persistence remains deliberately absent
- mechanical recreation of WPF/DataGrid-only layout persistence where the Avalonia surface exposes no equivalent user-resizable/reorderable state
- synthetic live-load validation of Queue Priority Ranking, 0-source Queue rotation and ListOrder pause/resume; these optional checks are waived for FirstLight because provoking suitable runtime conditions costs disproportionate time

FirstLight now serves as the first usable cross-platform AJCC client branch and as the validated migration surface for productive AJCC semantics. Packaging, release promotion and any merge to `main` remain separate explicit decisions.
