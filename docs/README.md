# Documentation

This directory holds architecture, migration, protocol and platform notes for the cross-platform AJCC project.

## Foundation documents

- `Foundation-v0.0.1.md` — repository boundaries, Foundation rules and first technical proof
- `Portability-Inventory-v0.0.1.md` — migration inventory of the current Windows/WPF AJCC: reusable Core code, platform abstractions, Desktop rewrites and orchestration that must be extracted from `MainWindow`

## Initial topics

- separation of platform-neutral AJCC logic from desktop UI
- migration map from the existing Windows/WPF repository
- platform service abstractions
- Windows/Linux/macOS behavior differences
- compatibility rules for the existing AppleJuice core and network

The first implementation milestone is **AJCC-X v0.0.1 — Foundation**.
