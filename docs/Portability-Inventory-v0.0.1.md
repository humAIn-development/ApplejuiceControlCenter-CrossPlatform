# AJCC-X v0.0.1 — Portability Inventory

## Scope

This inventory maps the current productive Windows/WPF AJCC repository to the cross-platform architecture. It is based on the current `humAIn-development/ApplejuiceControlCenter` `main` state reviewed for Foundation work.

The purpose is not to port files mechanically. The purpose is to identify:

1. code that can move into `AJCC.Core` with little or no functional change,
2. code that is reusable after a small architectural split,
3. operating-system integration that belongs behind platform abstractions,
4. WPF code that must be rebuilt in `AJCC.Desktop`, and
5. application logic currently trapped in `MainWindow` that should be extracted before a cross-platform UI is built.

## Executive conclusion

AJCC does **not** need its AppleJuice protocol knowledge to be rediscovered. A substantial part of the models, XML parsing, HTTP/XML client behavior, polling, AJFSP/AJL handling, state updates, media planning, share inspection and snapshot logic is already based on platform-neutral .NET APIs.

The principal migration cost is elsewhere:

- the existing project is explicitly Windows-only (`net10.0-windows`, WPF, Windows Forms, `win-x64`),
- the desktop UI is large and deeply WPF-specific,
- several operating-system services are implemented directly with Windows APIs, and
- substantial application/orchestration logic still lives inside `MainWindow.xaml.cs` instead of independent services.

Therefore the cross-platform project should preserve and test the reusable protocol/domain logic, extract orchestration into application services, and rebuild the desktop layer around those services. A line-by-line WPF-to-Avalonia conversion is explicitly rejected.

---

## Classification A — directly or nearly directly portable to `AJCC.Core`

These files contain no WPF, Windows Forms, Registry or Win32/P/Invoke dependency. Namespace/project references will change, but the underlying behavior is portable.

### Models

- `Models/AjModels.cs`
  - Core data models, `ObservableCollection`, `INotifyPropertyChanged`, derived display values.
  - `ObservableCollection` and `INotifyPropertyChanged` are standard .NET and not WPF-specific.
  - Candidate: `AJCC.Core/Models`.

- `Models/AjState.cs`
  - Runtime collections and Core state.
  - Candidate: `AJCC.Core/State`.

- `Models/AjLinkInfo.cs`
  - AJFSP link model and derived source/display values.
  - Candidate: `AJCC.Core/Links`.

- `Models/AjStartupImportRequest.cs`
  - Startup AJFSP/AJL request container.
  - Candidate: `AJCC.Core/Links`.

- `Models/ShareSafetyReport.cs`
  - Share safety input/output models.
  - Candidate: `AJCC.Core/Features/ShareSafety`.

- `Models/ShareSnapshotModels.cs`
  - Snapshot/comparison domain models.
  - Candidate: `AJCC.Core/Features/ShareSnapshot`.

### Helpers

- `Helpers/DisplayFormatHelper.cs`
  - Platform-neutral formatting.

- `Helpers/NaturalStringComparer.cs`
  - Platform-neutral natural sorting. The existing source already describes it as UI-independent.

- `Helpers/PowerDownloadFactorHelper.cs`
  - Pure PowerDownload factor normalization/conversion.

- `Helpers/SecurityHelper.cs`
  - MD5 conversion and diagnostic privacy masking using standard .NET APIs.
  - Already handles both Windows-like and Unix-like path masking.

- `Helpers/XmlHelper.cs`
  - LINQ-to-XML helpers.

- `Helpers/CoreTargetPathSanitizer.cs`
  - Pure Core-target-path normalization. Separator is already explicit in important methods.

### Parsing / protocol / state

- `Parsers/AjXmlParser.cs`
  - Settings, shares, information, modified state, downloads, uploads, users, servers, searches and related XML parsing.
  - Candidate: `AJCC.Core/Protocol/Parsing`.

- `Services/AjCoreCompatibilityProfile.cs`
  - Core-version capability rules.

- `Services/AjEndpoints.cs`
  - AppleJuice HTTP/XML endpoint constants.

- `Services/AjLinkParser.cs`
  - AJFSP parsing and legacy encoded-separator compatibility.

- `Services/AjLinkListParser.cs`
  - AJL file/line parsing.

- `Services/AjProcessLinkResult.cs`
  - Core `processlink` result classification.

- `Services/AjStartupArgumentParser.cs`
  - AJFSP/AJL startup argument classification.

- `Services/AjStateUpdater.cs`
  - Runtime state merge/update logic.

- `Services/AjfspLinkBuilder.cs`
  - AJFSP plain/URI link generation.

- `Services/AjPollingService.cs`
  - Core `modified.xml` polling, session handling, reconnect/error state and full-resync signalling.

- `Services/CollectorService.cs`
  - Periodic information collection.

### Feature logic

- `FeedbackSubmissionService.cs`
  - HTTP multipart feedback submission and optional diagnostics ZIP upload.
  - No WPF/Windows dependency.

- `Services/ShareSafetyInspector.cs`
  - Analyses supplied Core/share paths and filenames; does not require Windows filesystem traversal.

These files form the first migration batch because they can establish a real, testable `AJCC.Core` without a desktop UI.

---

## Classification B — reusable after a small architectural refactor

### `Services/AppleJuiceCoreClient.cs`

The client is fundamentally portable. It uses `HttpClient`, compression, XML, strings and standard .NET networking APIs. Core commands and simulation logic can be retained.

However the connection model is currently hard-coded to:

```csharp
return $"http://{Host}:{Port}{path}?{query}";
```

For AJCC-X this should become an explicit endpoint/base-URI model, for example:

```text
CoreEndpoint
  Scheme
  Host
  Port
  BasePath
```

or a validated `Uri BaseUri`.

This provides three benefits at once:

- platform-neutral Core access,
- cleaner unit/integration testing through injected `HttpClient`, and
- first-class HTTPS/reverse-proxy/base-path support instead of carrying the current `Host + Port + http://` limitation into the new architecture.

**Target:** `AJCC.Core/Protocol`.

### `Services/AppConfigService.cs`

JSON and file I/O are portable, but the service currently combines three responsibilities:

- application-data path selection,
- JSON configuration persistence,
- Windows DPAPI credential protection.

Split into:

```text
IAppDataPathProvider
ICredentialStore
configuration serialization/storage
```

The JSON store may be shared; credential storage must be platform-specific.

### `Models/CoreConnectionSettings.cs`

Profile selection, host/port normalization and legacy-profile migration are reusable. The model should no longer define a persisted field whose semantics are explicitly "DPAPI-protected password bound to the current Windows user".

For AJCC-X:

- profile/domain metadata remains in Core/Application,
- runtime credentials are supplied by a credential service,
- old Windows settings can be imported by a compatibility/migration path.

### `Services/ShareSnapshotService.cs`

Most of the snapshot algorithm is portable: JSON, GZip, SHA-256, compare logic and notices use standard .NET.

Only storage-root selection is currently coupled to `ApplicationData/AppleJuiceCommunityWpf/share-snapshots` and should be injected.

Positive finding: the comparison algorithm already distinguishes Windows-like and Unix-like paths and applies case-insensitive comparison only to Windows-like paths. That logic should be retained.

### `Models/MediaDownloadTargetPlan.cs` + `Services/MediaDownloadTargetPlanner.cs`

The planner is valuable reusable business logic and has no WPF dependency. Before migration, Core target paths should be made explicit rather than relying on host-OS `Path` semantics or Windows-style `\\` defaults.

Introduce a small Core-path utility/abstraction so a Linux AJCC still creates path strings compatible with the connected AppleJuice Core rather than accidentally adopting local Linux filesystem semantics.

### `Services/AjSingleInstanceService.cs`

Mutex/named-pipe transport is reusable in concept, but the current service accepts a WPF `Dispatcher` and dispatches incoming arguments through it.

Split:

- single-instance/argument transport,
- UI-thread dispatch.

The desktop layer can then use the Avalonia dispatcher without the transport service referencing a UI framework.

Its diagnostics storage path should also use the application-data path abstraction.

### `Services/AppThemeService.cs`

Theme names and color definitions are reusable as data. Applying them is WPF-specific (`Application.Resources`, WPF brushes/system colors).

Recommendation:

- keep theme definitions/palette in Desktop,
- rewrite theme application for Avalonia,
- do not place WPF resource manipulation in Core.

### `Models/AppUiConfiguration.cs`

The class technically compiles without WPF, but it mixes unrelated concerns:

- window placement, theme, footer and colors,
- tray/sound/notification settings,
- external VLC/local paths,
- download queue and PowerDownload automation policy.

Do not copy it wholesale. Split it into:

```text
Application settings
  DownloadQueue
  PowerDownload policy

Desktop settings
  Theme
  Window placement
  Footer
  Colors
  Tooltips

Platform/integration settings
  Tray/notifications
  external applications
  local filesystem mappings
```

### `Models/LocalPathConfiguration.cs`

The type is portable C#, but its values (`LocalIncomingDirectory`, VLC executable, external preview) describe local desktop/platform integration. Keep it outside the protocol/domain core.

### `AppBuildInfo.cs`

The assembly metadata mechanism is portable. AJCC-X should create its own build metadata rather than copying the Windows product/version constants.

---

## Classification C — operating-system integration requiring platform services

### Credentials

Current implementation: `Helpers/WindowsDpapiCredentialProtector.cs`

It P/Invokes Windows `crypt32.dll` (`CryptProtectData` / `CryptUnprotectData`) and `kernel32.dll` (`LocalFree`). It cannot move to Core.

Create:

```csharp
ICredentialStore
```

with platform implementations later:

- Windows: DPAPI or equivalent secure Windows store,
- Linux: Secret Service/keyring compatible implementation,
- macOS: Keychain.

### AJFSP/AJL protocol and file association

Current implementation: `Services/AjProtocolRegistrationService.cs`

It reads/writes Windows Registry URL/file associations and ProgIds. Replace with:

```csharp
IProtocolRegistrationService
```

Platform implementations own Windows Registry, Linux desktop/xdg/MIME registration, and macOS URL/file association behavior.

### Notifications and tray

Current implementation: `Services/NotificationFeedbackService.cs`

It uses Windows Forms `NotifyIcon` and WPF `Application`. Split into:

```text
INotificationService
ITrayService
```

### Audio feedback

Current implementation: `Services/AudioFeedbackService.cs`

It uses WPF pack resources and `System.Media.SoundPlayer`. Replace with a desktop/platform audio abstraction or Avalonia-compatible implementation.

### Local filesystem navigation

Current implementation: `Services/WindowsDirectoryTreeProvider.cs`

The class is explicitly based on Windows drive/root/path semantics. Replace with:

```csharp
ILocalDirectoryProvider
```

The Core-facing directory browser (`directory.xml`) remains separate from the local machine filesystem browser.

### Packaging / publishing

The existing Windows project is intentionally restricted to:

```text
net10.0-windows
UseWPF=true
UseWindowsForms=true
win-x64
```

Inno Setup and the current PowerShell release pipeline remain Windows-product artifacts. AJCC-X needs independent publish/package paths for Windows, Linux and macOS.

---

## Classification D — rebuild in `AJCC.Desktop`, do not mechanically migrate

The following group is WPF UI or WPF-specific presentation infrastructure and should be recreated around Avalonia views/view-models/services:

- `App.xaml`, `App.xaml.cs`
- `MainWindow.xaml`, `MainWindow.xaml.cs`
- all `MainWindow.*.cs` partial UI modules
- `AppMessageBox*`
- `ConfirmWindow*`
- `CoreDirectoryPickerWindow*`
- `CoreProfileManagerWindow*`
- `FeedbackWindow*`
- `LinkImportWindow*`
- `LoginWindow*`
- `MediaDownloadTargetPlannerWindow*`
- `MediaFormatBuilderWindow*`
- `PartListWindow*`
- `PdlFactorInputWindow*`
- `PromptWindow*`
- `ShareSafetyInspectorWindow*`
- `ShareSnapshotDiffWindow*`
- `AppleProgressBar.cs`
- `UploadSpeedSparkline.cs`
- `DataGridColumnExtentHelper.cs`
- `DownloadStatusColorConverters.cs`
- `DownloadStatusColorDialog.cs`
- `FooterConfigurationWindow.cs`
- `Helpers/NaturalStringSortHelper.cs`

`NaturalStringSortHelper` is a useful example of the boundary: the underlying `NaturalStringComparer` is portable, while the current helper directly manipulates WPF `DataGrid`, bindings and collection views. Reuse the comparer, rebuild the grid adapter.

---

## Main architectural debt — application logic inside `MainWindow`

The current `MainWindow.xaml.cs` is not merely a view. It owns protocol clients, state, timers, queues, caches, reachability, automation and filesystem services in addition to WPF controls.

This is the most important migration finding.

The following responsibilities should be extracted into platform-neutral application services before or while their UI is rebuilt:

### `CoreProfileCoordinator`

Extract:

- profile reachability probing,
- switching,
- automatic failover,
- active-profile state,
- coordination with credential/config persistence.

### `LinkImportCoordinator`

Extract:

- queued AJFSP/AJL imports,
- debounce/quiet-window behavior,
- coordination with profile switching/failover,
- Core submission and result tracking.

The visual startup sync overlay remains Desktop-only.

### `DownloadAutomationService`

Extract the current queue/PowerDownload policy from `ApplyDownloadAutomationPoliciesAsync` and related MainWindow state.

UI should only edit policy settings and display state; the service should decide which Core commands are required.

### `PartListAggregationService`

Extract:

- download partlist loading,
- user/source partlist loading,
- caching,
- active transfer ranges,
- aggregated part-state construction.

The graphical partlist is Desktop-only.

### `ServerReachabilityService`

Extract server probes, reconnect restriction state and timers from the Window.

### `SearchCoordinator`

Extract Core search lifecycle and search state. WPF collection-view filtering, selection and DataGrid behavior remain Desktop-only.

### `ShareCoordinator`

Extract share loading/reload state, large-share batching, filtering source data and Core directory/share operations. The tree/grid presentation belongs to Desktop.

### `UpdateCheckService`

HTTP/JSON update metadata lookup is application logic. Dashboard text and tile visibility are view concerns.

### `DiagnosticsService`

Diagnostic gathering/export should not be tied to the Window. Existing `SecurityHelper` masking can be reused.

### Upload name resolution

`MainWindow.UploadFiltering.cs` currently combines view buckets with useful application logic that resolves missing upload names through `getobject.xml` and caches results. Split the Core lookup/cache behavior from active/inactive UI filtering.

---

## Proposed AJCC-X target layout after extraction

```text
src/AJCC.Core/
  Models/
  Protocol/
    CoreEndpoint
    AjEndpoints
    AppleJuiceCoreClient
    Parsing/
  State/
    AjState
    AjStateUpdater
    AjPollingService
  Links/
    AjLinkParser
    AjLinkListParser
    AjfspLinkBuilder
    AjProcessLinkResult
  Application/
    CoreProfileCoordinator
    DownloadAutomationService
    LinkImportCoordinator
    PartListAggregationService
    SearchCoordinator
    ShareCoordinator
    ServerReachabilityService
    UpdateCheckService
  Features/
    MediaPlanner/
    ShareSafety/
    ShareSnapshot/
  Diagnostics/

src/AJCC.Platform/
  Abstractions/
    ICredentialStore
    IProtocolRegistrationService
    INotificationService
    ITrayService
    IAudioFeedbackService
    ILocalDirectoryProvider
    IAppDataPathProvider
    IExternalLauncher
    ISingleInstanceService
  Windows/
  Linux/
  MacOS/

src/AJCC.Desktop/
  App/
  Views/
  ViewModels/
  Controls/
  Themes/
```

The exact project split can evolve, but the dependency direction is fixed:

```text
AJCC.Desktop -> AJCC.Core
AJCC.Desktop -> AJCC.Platform abstractions/implementations
AJCC.Core    -X-> WPF / WinForms / Registry / Win32
```

---

## Recommended migration order

### Foundation migration block 1 — create the real Core library

Create `src/AJCC.Core/AJCC.Core.csproj` as a normal `net10.0` class library with no Windows target and no UI framework references.

Migrate first:

1. reusable helpers (`DisplayFormatHelper`, `NaturalStringComparer`, `PowerDownloadFactorHelper`, `SecurityHelper`, `XmlHelper`, `CoreTargetPathSanitizer`),
2. core/link/share models (`AjModels`, `AjState`, `AjLinkInfo`, `AjStartupImportRequest`, share safety/snapshot models),
3. `AjXmlParser`,
4. endpoint/link/compatibility/state services,
5. AJFSP/AJL parsing/building,
6. polling once the Core client is available.

### Foundation migration block 2 — tests before UI

Add tests for at least:

- known `settings.xml` parsing,
- `information.xml` parsing,
- representative `modified.xml` parsing/state merge,
- AJFSP parse/build round trips,
- AJL parsing,
- Core compatibility profile behavior,
- path sanitization,
- PowerDownload factor conversion.

### Foundation migration block 3 — endpoint/client refactor

Introduce `CoreEndpoint`/BaseUri support and migrate `AppleJuiceCoreClient` with injected/testable HTTP transport. Preserve existing legacy query encoding behavior.

This is where HTTPS/reverse-proxy/base-path support should be designed once, correctly.

### Foundation technical proof

Before Avalonia UI work starts, prove that the new `AJCC.Core` can:

1. connect to an existing AppleJuice Core,
2. authenticate using the current password/hash rules,
3. read and parse settings/information/session data,
4. start polling and apply a `modified.xml` response to `AjState`.

Only after this proof should AJCC-X v0.0.2 begin the first Avalonia shell.

---

## Migration rule

A file being technically cross-platform does not automatically mean it belongs in `AJCC.Core`.

The placement test is:

> Does this code describe AppleJuice/domain/application behavior, or does it describe how one desktop operating system presents/stores/integrates that behavior?

The first belongs in Core/Application. The second belongs in Platform/Desktop.

This boundary is more important than preserving the old file layout.
