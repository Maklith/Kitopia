# Architecture boundaries

Paths in backticks are relative to the repository root unless stated otherwise.

These rules are enforced by `tests/KitopiaTest/Architecture/`, especially `ProjectDependencyBoundaryTests.cs` and `LayeringConventionTests.cs`.

- `Kitopia.Feature` must remain independent of Avalonia, desktop/mobile hosts, native APIs, OS branches and platform compilation symbols.
- `Kitopia.Feature.Avalonia` references `Kitopia.Feature` only as a project dependency. Keep native APIs, OS branches and host types out of this reusable UI layer. Its `Controls/HoverFlyoutButton` owns hover-card dismissal for device compatibility, desktop hotkeys and Everything status: keep the card open over the indicator or content, allow a short crossing delay, and close it on exit or detachment. `Views/DeviceCommunication/DeviceCommunicationStyles.axaml` owns the shared warning palette and `KitopiaWarningIndicatorButton`, `KitopiaWarningFlyoutPresenter`, `KitopiaWarningCard` and `KitopiaWarningActionButton` themes. Reuse these framework control themes; callers supply their header, message content and actions without duplicating the warning shell or introducing forwarding controls.
- Mobile projects must not reference desktop projects or `PluginCore`, including transitively or through linked source/AXAML.
- `Kitopia.Desktop.Abstractions` contains real BCL-only contracts, with no package or project references.
- The plugin SDK must not depend on a host, desktop abstractions or platform implementation. Preserve public names, signatures and the `PluginCore` assembly identity.
- Desktop features must not depend on Windows/Linux adapters or mobile hosts. The approved project references are desktop abstractions, the plugin SDK, shared feature/UI projects, NodifyM and Pinyin.
- Windows and Linux adapters must not depend on each other or mobile hosts. The desktop composition root must not depend on a mobile host.
- Inject platform behavior through the owning contracts and register implementations in the host. Keep reusable logic in its existing feature layer.

## Contracts

Consult the source for exact signatures instead of maintaining a duplicate method catalog.

- Desktop lifecycle/config/navigation/account/UI contracts: `src/Kitopia.Desktop.Features/Services/Interfaces/`, including `IApplicationService`, `IConfigService`, `INavigationService`, `IAccountService`, and `IHotKetImpl`.
- `IConfigService.Version` and `ConfigManger.Version` use `NuGet.Versioning.NuGetVersion`, preserving prerelease labels for host dependency checks. Desktop update comparisons use semantic version precedence and ignore build metadata.
- Native desktop operations: `IDesktopPlatformInfo`, `Shell/IDesktopShell`, `FileSystem/IFileLockService`, and `TextSelection/ITextSelectionService` in `src/Kitopia.Desktop.Abstractions/`.
- Scenario editor/upload contracts: `src/Kitopia.Desktop.Features/CustomScenario/Services/`; plugin operations: `PluginHost/Contracts/`.
- Public extension points: `IPlugin`, `IPluginManger`, clipboard, screenshot, OCR, translation, search, toast, inference and scenario contracts in `src/Kitopia.Desktop.PluginSdk/`.
- Window topmost operations go through `IWindowTool`. Its existing two-argument method retains the interactive border/action overlays; the overload with `showOverlay: false` restores native topmost state without overlays or focus changes, including for selection translation. Keep platform calls in the platform service.
- Explorer COM contracts: `src/ContextMenuDll/Interop/ComInterfaces.cs`.

Specialized contracts: [Search and indexing](indexing.md), [Inference](inference.md), [Device communication](device-communication.md).
