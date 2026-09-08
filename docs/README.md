# Documentation

This directory holds architecture, migration, protocol and platform notes for the cross-platform AJCC project.

## Current milestone

- `FirstLight-v0.0.2.md` — chronological FirstLight development record, Core/UI boundary decisions, compatibility rules, live-validation history and the current v0.0.2 checkpoint

AJCC-X is currently on **v0.0.2 — FirstLight**. The controlled productive-semantic reverse backfill has reached the earliest reliably documented boundary. Release hardening now validates the exact PR source head on Ubuntu, Windows and macOS with a warning-free Release build, 230 Core tests and 3 headless Desktop-service persistence tests per operating system.

## Foundation documents

- `Foundation-v0.0.1.md` — repository boundaries, Foundation rules and first technical proof
- `Portability-Inventory-v0.0.1.md` — migration inventory of the Windows/WPF AJCC: reusable Core code, platform abstractions, Desktop rewrites and orchestration to extract from `MainWindow`

## Continuing rules

- keep platform-neutral AJCC logic separate from the desktop UI
- use the productive Windows/WPF AJCC as the behavioral reference for existing functionality
- preserve AppleJuice Core/network compatibility rather than redesigning protocol semantics
- keep `AJCC.Core` free of WPF, WinForms, registry, Win32 UI and Avalonia dependencies
- isolate genuine host-OS integration behind desktop/platform boundaries
- do not merge FirstLight to `main` without Martin's explicit decision
