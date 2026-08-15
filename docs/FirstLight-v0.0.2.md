# AJCC-X v0.0.2 — FirstLight

## Purpose

FirstLight is the first real cross-platform desktop shell on top of the validated `AJCC.Core` Foundation. It is intentionally a new UI layer, not a mechanical WPF conversion.

The productive Windows/WPF AJCC remains untouched.

## Branch model

- base: `foundation/v0.0.1-foundation`
- development: `firstlight/v0.0.2-firstlight`
- `main` remains untouched until Martin explicitly decides otherwise

## UI framework

FirstLight uses Avalonia 12.1.1 and targets .NET 10.

`AJCC.Desktop` references `AJCC.Core`; `AJCC.Core` does not reference Avalonia or any other desktop framework.

## First vertical slice

The initial FirstLight window provides:

- absolute Core endpoint input (`http` / `https`, optional port and base path)
- transient Core password entry without repository/config persistence
- connection validation through the existing Foundation transport
- runtime bootstrap through `CoreRuntimeBootstrapper`
- continuous `modified.xml` polling through `AjPollingService`
- dashboard data for Core identity/version, network size, credits, transfer speeds and list counts
- live download list
- live upload list
- live server list
- live search list and result list
- starting searches through the real `/function/search` endpoint
- cancelling the selected running search through `/function/cancelsearch`

## Search protocol compatibility

The productive AJCC currently sends search requests as HTTP POST requests and encodes the search query with the legacy Java/phpGUI-compatible RFC3986-style behavior where spaces are `%20` rather than `+`.

That behavior is migrated into the platform-neutral `AppleJuiceCoreClient` and covered by Core tests. Transport still uses `CoreEndpoint`, so HTTPS and reverse-proxy base paths remain supported.

## Endpoint parsing

`CoreEndpoint.Parse` / `CoreEndpoint.FromUri` centralize validation for desktop and tool callers. Embedded credentials, query strings and fragments are rejected; credentials stay separate from the technical endpoint URI.

## Thread boundary

`AjPollingService` performs network polling away from the desktop UI. FirstLight applies each parsed `ModifiedParseResult` to the bound `AjState` on Avalonia's UI dispatcher before ObservableCollection/model changes reach controls.

## CI gate

The existing GitHub Actions matrix is extended to FirstLight and builds the complete solution on:

- Windows
- Linux
- macOS

Core regression tests remain part of every matrix job. A successful CI build proves the Avalonia desktop project compiles for all three desktop targets; actual window/runtime behavior still requires local launch validation.

## Intentionally not included yet

- installer / packaging
- tray integration
- OS credential stores
- protocol/file associations
- persisted desktop settings
- themes beyond Avalonia Fluent default
- advanced download/server actions
- dialogs and file pickers
- parity with the productive WPF feature surface

FirstLight exists to prove the UI-to-Core boundary and then expand in controlled vertical slices.
