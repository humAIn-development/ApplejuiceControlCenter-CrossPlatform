# AJCC-X v0.0.1 — Foundation

## Purpose

Create a clean cross-platform foundation without destabilizing the productive Windows/WPF AJCC.

## Initial repository boundaries

### `src/AJCC.Core`
Platform-neutral code only. Intended contents include models, XML parsing, AppleJuice core HTTP/XML communication, polling, AJFSP/AJL handling and reusable application logic.

### `src/AJCC.Platform`
Operating-system integration and abstractions. Intended areas include credential storage, protocol/file associations, notifications, local file-system access and external application launching.

### `src/AJCC.Desktop`
Cross-platform desktop UI. The intended UI technology is Avalonia, but framework packages are deliberately not pinned during the repository bootstrap.

### `tests/AJCC.Core.Tests`
Tests for platform-neutral behavior. The test framework will be selected when the first production code is migrated.

## Foundation rules

1. The existing Windows AJCC remains the productive client.
2. No Windows-specific API belongs in `AJCC.Core`.
3. Platform-specific behavior must be isolated behind explicit services or interfaces.
4. Existing AppleJuice protocol behavior is preserved unless a change is deliberately specified and tested.
5. Features are migrated vertically and testably instead of porting the current WPF `MainWindow` wholesale.

## First technical proof

The first useful milestone after scaffolding is a platform-neutral connection to an existing AppleJuice core and successful parsing of basic core information without any desktop UI dependency.
