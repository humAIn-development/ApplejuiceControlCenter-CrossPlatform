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

The FirstLight window provides:

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

Search cancellation is deliberately not exposed. Although the historical endpoint inventory contains `/function/cancelsearch`, real Core behavior does not reliably abort a running search, so FirstLight must not present that as a working function.

## Search protocol compatibility

The productive AJCC currently sends search requests as HTTP POST requests and encodes the search query with the legacy Java/phpGUI-compatible RFC3986-style behavior where spaces are `%20` rather than `+`.

That behavior is migrated into the platform-neutral `AppleJuiceCoreClient` and covered by Core tests. Transport still uses `CoreEndpoint`, so HTTPS and reverse-proxy base paths remain supported.

## Endpoint parsing

`CoreEndpoint.Parse` / `CoreEndpoint.FromUri` centralize validation for desktop and tool callers. Embedded credentials, query strings and fragments are rejected; credentials stay separate from the technical endpoint URI.

## Thread boundary

`AjPollingService` performs network polling away from the desktop UI. FirstLight applies each parsed `ModifiedParseResult` to the bound `AjState` on Avalonia's UI dispatcher before ObservableCollection/model changes reach controls.

## AJCC visual language

FirstLight no longer relies on the unmodified Avalonia Fluent appearance. Its first application-level style layer reuses the current productive AJCC visual language without porting WPF controls or templates:

- background `#15171C`
- primary panel `#20232B`
- secondary panel `#282C35`
- accent `#4DA3FF`
- text `#F3F6FA`
- muted text `#AEB7C2`
- input background `#111318`
- selection `#355A86`
- compact Fluent density
- AJCC-like rounded inputs, buttons, metric cards, list areas and tab states

This is a visual compatibility layer only. It does not introduce WPF dependencies into the cross-platform desktop project or Core.

## Windows live validation — 2026-08-15

FirstLight was launched locally on Windows and tested against the real password-protected `AJ-Core1` at `http://127.0.0.1:8851/`.

Validated behavior:

- Avalonia application launch: OK
- password-protected Core connection: OK
- Core identity: `AJ-Core1`
- Core version: `0.31.149.113`
- network counters populated from the Core and updated live
- `9` servers visible in runtime state during the test
- Core timestamp advanced continuously through `modified.xml` polling
- download/upload speed fields updated from live information
- a real search for `linux` was submitted from FirstLight
- the search appeared in live state and returned results; `30` hits were visible at the captured test point
- result filenames, sizes and user counts were rendered in the Avalonia search result list

The first connection attempt exposed a desktop-only `NullReferenceException` caused by the generated password-control field being null in the click handler. Commit `b629ab78c63a5844d74892835832ef7b0b80dbef` replaced that fragile field access with Avalonia namescope lookup; the subsequent live connection succeeded and remained stable.

The later AJCC-style visual pass is compile/test validated by CI and still requires a local visual check after pulling the current branch.

## CI gate

The GitHub Actions matrix builds the complete solution on:

- Windows
- Linux
- macOS

Core regression tests remain part of every matrix job. The AJCC-style FirstLight head `ece7125c1029501046ce2deec96565c5a118c440` passed restore, complete Release build and Core tests on all three runners in workflow run `31890377932`.

## Intentionally not included yet

- installer / packaging
- tray integration
- OS credential stores
- protocol/file associations
- persisted desktop settings
- light-theme/theme switching
- advanced download/server actions
- dialogs and file pickers
- parity with the productive WPF feature surface

FirstLight exists to prove the UI-to-Core boundary and then expand in controlled vertical slices.
