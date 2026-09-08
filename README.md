# ApplejuiceControlCenter-CrossPlatform

Cross-platform successor project for the Applejuice-Control-Center (AJCC).

## Status

**AJCC-X v0.0.2 FirstLight / pre-alpha.** FirstLight is the first usable cross-platform Avalonia client on top of the platform-neutral `AJCC.Core`. The productive Windows/WPF AJCC remains the reference implementation and is developed independently.

Development continues on `firstlight/v0.0.2-firstlight`. `main` remains untouched and PR #2 remains draft until Martin explicitly decides otherwise.

## Goal

Build one AJCC desktop client for Windows, Linux and macOS while keeping the AppleJuice core/protocol logic platform-neutral and isolating operating-system-specific integration behind dedicated platform services.

## Architecture

- `src/AJCC.Core` — platform-neutral models, parsers, Core communication, polling, AJFSP and application logic
- `src/AJCC.Desktop` — Avalonia desktop UI for Windows, Linux and macOS
- `src/AJCC.Platform` — reserved boundary for platform-specific integration that cannot remain portable
- `tests/AJCC.Core.Tests` — regression tests for the platform-neutral Core layer
- `tests/AJCC.Desktop.Tests` — headless cross-platform tests for persistent Desktop services
- `docs` — architecture, migration, compatibility and milestone notes

`AJCC.Core` must not depend on WPF, WinForms, the Windows registry, Win32 UI APIs or Avalonia.

## Current FirstLight checkpoint

FirstLight now covers the major productive AJCC workflows, including Core connection/bootstrap and polling, Core profiles/failover, downloads/uploads/search/server/share workflows, Core settings with readback, Core-side directory browsing, Share-directory draft semantics, AJFSP/AJL import/export, local Incoming mapping, external VLC safety, diagnostic ZIP export and integrated feedback.

Core passwords remain transient and are deliberately not persisted. UI preferences that are safe to persist are stored under the AJCC-X settings area.

The controlled historical reverse backfill has reached the earliest reliably documented productive boundary. The current validated runtime/test head is `1f71298c8fc0cb5e85f8d45994cb23a6c6fd1d5f`; CI #522 / run `33739383122` passed Restore, Release Build, 230 Core tests and 3 headless Desktop-service tests on Ubuntu, macOS and Windows. The release build remains warning-free, CI identifies the exact PR source head, persistent Desktop JSON settings use atomic replacement, persisted startup arguments are privacy-sanitized, single-instance import IPC is restricted to the current OS user, and feedback technical context plus the anonymized diagnostics ZIP require an explicit opt-in that defaults to off.

Installer/packaging and merge-to-`main` are separate later decisions and are not implied by the current FirstLight checkpoint.

## Milestones

- **AJCC-X v0.0.1 — Foundation**: repository boundaries and first platform-neutral Core extraction
- **AJCC-X v0.0.2 — FirstLight**: current cross-platform desktop client and productive-semantic backfill

See `docs/FirstLight-v0.0.2.md` for the detailed milestone history and current scope.
