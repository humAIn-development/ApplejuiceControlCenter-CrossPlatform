# AJCC-X v0.0.1 — Foundation

## Purpose

Create a clean cross-platform foundation without destabilizing the productive Windows/WPF AJCC.

AJCC-X is not a WPF-to-Avalonia source conversion. Existing AppleJuice knowledge is extracted into a platform-neutral Core, tested there, and only then consumed by a future desktop layer.

## Repository boundaries

### `src/AJCC.Core`
Platform-neutral `net10.0` code only: protocol/domain models, helpers, XML parsing, AppleJuice HTTP/XML communication, runtime state, polling and AJFSP/AJL behavior.

### `src/AJCC.Platform`
Reserved for operating-system integration and abstractions such as credential storage, protocol/file associations, notifications, local file-system access and external application launching.

### `src/AJCC.Desktop`
Reserved for the cross-platform desktop UI. Avalonia remains the intended candidate, but no UI framework is part of Foundation.

### `tests/AJCC.Core.Tests`
MSTest regression and architecture tests for platform-neutral Core behavior.

### `tools/AJCC.Core.Probe`
GUI-free integration probe for validating the Foundation Core against a real AppleJuice Core without putting passwords on the command line.

## Foundation rules

1. The existing Windows AJCC remains the productive client.
2. No Windows-specific API belongs in `AJCC.Core`.
3. Platform-specific behavior must be isolated behind explicit services or interfaces.
4. Existing AppleJuice protocol behavior is preserved unless a change is deliberately specified and tested.
5. Features are migrated vertically and testably instead of porting the current WPF `MainWindow` wholesale.
6. `main` is not modified by autonomous Foundation development; PR #1 remains unmerged until Martin explicitly decides.

## Implemented Core foundation

### Links and compatibility

- `AjLinkInfo`
- `AjLinkParser`
- `AjfspLinkBuilder`
- `AjLinkListParser`
- `AjStartupArgumentParser`
- `AjProcessLinkResult`
- `AjCoreCompatibilityProfile`
- `AjEndpoints`

Legacy AJFSP encoded separators, AJL three-line blocks and the existing `processlink` compatibility threshold are retained.

### Portable helpers

- `DisplayFormatHelper`
- `PowerDownloadFactorHelper`
- `NaturalStringComparer`
- `CoreTargetPathSanitizer`
- `XmlHelper`
- `SecurityHelper`

### Protocol/runtime models and state

- protocol/runtime subset of the productive AJ models
- `AjState`
- `AjXmlParser`
- `AjStateUpdater`

The current productive `AjModels.cs` was not copied mechanically. During migration, `CollectorShareTypeStat` was found to contain a direct `System.Windows.Media.Brush` dependency. That desktop-specific coupling is intentionally excluded from `AJCC.Core`. `AjDirectoryTreeItem` is likewise not needed for the Core proof and remains outside the migrated runtime model set.

### Core endpoint model

`CoreEndpoint` replaces the old assumption that a Core is always `http://Host:Port`.

It supports:

- `http`
- `https`
- hostname or IP
- optional port
- optional base path
- a display identity separate from the technical URI

Examples:

```text
http://127.0.0.1:9851/
https://core.example.org/
https://example.org/applejuice/
```

### HTTP/XML client

The Foundation `AppleJuiceCoreClient` uses `CoreEndpoint` and an injectable `HttpClient`.

Implemented for the first technical proof:

- `settings.xml`
- `information.xml`
- `getsession.xml`
- `modified.xml`
- password hashing compatible with the existing client behavior
- gzip/deflate response decoding
- privacy-masked diagnostics
- connection-shape validation for AppleJuice `settings.xml`

The old hard-coded `http://Host:Port` URL construction is deliberately not carried over.

### Runtime bootstrap

`CoreRuntimeBootstrapper` performs a GUI-free initial sequence:

1. read and parse `settings.xml`
2. read and parse `information.xml`
3. obtain a session through `getsession.xml`
4. request initial `modified.xml`
5. apply the result to `AjState`

### Runtime polling

`AjPollingService` performs repeated `modified.xml` polling with:

- existing/new session handling
- Core timestamp tracking
- Java-style modified filters retained from the productive client
- connection degraded/restored/lost signalling
- repeated missing-timestamp full-resync signalling

## Cross-platform verification

GitHub Actions builds and tests the same solution on:

- `windows-latest`
- `ubuntu-latest`
- `macos-latest`

The CI gate performs restore, Release build and MSTest execution with .NET 10.

Architecture tests additionally fail if `AJCC.Core` directly references Windows desktop framework assemblies such as WPF or WinForms.

This verifies the source/build/test portability of the Foundation Core. GitHub-hosted runners cannot reach a developer's local Core, so live protocol validation is performed separately with `AJCC.Core.Probe`.

## Real-Core integration probe

Without Core password:

```text
dotnet run --project tools/AJCC.Core.Probe -- --endpoint http://127.0.0.1:9851/
```

With a password, set `AJCC_CORE_PASSWORD` in the local environment first. The password is never accepted as a normal command-line argument.

Reverse proxy / HTTPS can be tested directly, for example:

```text
dotnet run --project tools/AJCC.Core.Probe -- --endpoint https://example.org/applejuice/
```

A successful Probe confirms against the real Core:

- connection/settings access
- information parsing
- session creation
- initial modified-state load
- a subsequent live `modified.xml` polling cycle

### First live validation — 2026-08-15

The Foundation probe was successfully executed on Windows against an actual local AppleJuice Core instance (`AJ-Core1`) using a password-protected XML interface.

Validated endpoint and Core data:

- endpoint: `http://127.0.0.1:8851/`
- AppleJuice Core version: `0.31.149.113`
- XML port reported by `settings.xml`: `8851`
- connection validation: OK
- runtime bootstrap: OK
- network information parsing: OK (`119` users / `4,330,501` files at probe time)
- initial state: `0` downloads / `0` uploads / `9` servers / `0` searches
- session acquisition: OK
- Core timestamp acquisition: OK
- subsequent `modified.xml` polling cycle: OK
- probe result: `Foundation Core-Probe erfolgreich.`

The password itself is deliberately not recorded in the repository. Authentication was supplied through `AJCC_CORE_PASSWORD` and accepted by the real Core.

This completes the first live Foundation technical proof: the platform-neutral Core is not only build/test portable in CI, but can bootstrap and poll a real existing AppleJuice Core through the migrated HTTP/XML path.

## Foundation technical-proof status

Completed:

- platform-neutral `AJCC.Core`
- settings/information/session/modified transport and parsing
- runtime state population
- ongoing modified polling
- AJFSP parsing/building
- HTTP/HTTPS/base-path endpoint architecture
- Windows/Linux/macOS build and test gate
- architecture guard against direct Windows desktop dependencies
- live authenticated connection to a real AppleJuice Core
- live settings/information/session/bootstrap validation
- live `modified.xml` polling validation

The first Foundation technical proof is complete.

Still intentionally out of scope for Foundation:

- Avalonia UI
- tray integration
- themes
- installer/packaging
- protocol registration
- OS credential-store implementations
- desktop file dialogs/clipboard/notifications
