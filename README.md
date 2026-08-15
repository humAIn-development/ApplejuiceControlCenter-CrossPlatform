# ApplejuiceControlCenter-CrossPlatform

Cross-platform successor project for the Applejuice-Control-Center (AJCC).

## Status

**Foundation / pre-alpha.** This repository is being prepared as a clean cross-platform codebase. The existing Windows/WPF AJCC remains the productive client and is developed independently.

## Goal

Build one AJCC desktop client for Windows, Linux and macOS while keeping the AppleJuice core/protocol logic platform-neutral and isolating operating-system-specific integration behind dedicated platform services.

## Planned architecture

- `src/AJCC.Core` — platform-neutral models, parsers, core communication, polling, AJFSP and application logic
- `src/AJCC.Platform` — abstractions and platform-specific services such as credentials, protocol registration, notifications and local file-system integration
- `src/AJCC.Desktop` — cross-platform desktop UI
- `tests/AJCC.Core.Tests` — tests for the platform-neutral core
- `docs` — architecture, migration and protocol notes

## First milestone

**AJCC-X v0.0.1 — Foundation**

Establish the repository structure and isolate the first platform-neutral building blocks. No attempt is made to replace the productive Windows AJCC at this stage.
