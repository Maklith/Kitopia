# Repository map

Paths in backticks are relative to the repository root unless stated otherwise.

## Stack and entry points

- C#/.NET 10, Avalonia, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, and Serilog.
- Source projects live in `src/`, plugins in `plugins/`, tests and benchmarks in `tests/`, examples in `samples/`, third-party libraries in `external/`, and the Rust installer in `installer/`.
- Versions shared by desktop projects are defined in `KitopiaVersion.props` and imported through the root `Directory.Build.props`; plugin versions and manifests are maintained by their own projects.
- Solution files: `Kitopia.slnx` and `Kitopia.sln`. Architecture tests currently locate the repository using `Kitopia.sln`.
- Desktop composition root: `src/Kitopia.Desktop/Program.cs`; Avalonia lifecycle: `src/Kitopia.Desktop/App.axaml.cs`.
- Mobile composition: `src/Mobile/Kitopia.Mobile/App.axaml.cs` and `src/Mobile/Kitopia.Mobile/Services/`; native lifecycle code belongs to the Android/iOS hosts.
- Build automation: Fallout (`FalloutBuild`), implemented in `build/Build.cs` and `build/Build.*.cs`, launched by `build/build.cmd`, `build/build.ps1`, or `build/build.sh`.
- Main tests: `tests/KitopiaTest/KitopiaTest.csproj`, MSTest with Avalonia.Headless for UI tests.
- Windows installer and uninstaller: separate Rust crates under `installer/ModernInstallerR/`.

### Submodules

The following directories are Git submodules, not ordinary source folders:

- `src/Kitopia.Desktop.PluginSdk/`
- `external/NodifyM.Avalonia/`
- `external/PinyinM.NET/`
- `installer/ModernInstallerR/`

Initialize missing submodules with `git submodule update --init --recursive`. Inspect their working trees before edits; preserve local changes and avoid accidental submodule pointer updates.

## Projects and responsibilities

| Project or directory | Responsibility |
| --- | --- |
| `src/Kitopia.Feature/` | UI- and platform-neutral reusable feature logic. Communication types use `Kitopia.Feature.DeviceCommunication.*` namespaces; `Localization/` and `lang/` provide shared language resources. |
| `src/Kitopia.Feature.Avalonia/` | Reusable Avalonia chat views, templates, view models and localization integration. |
| `src/Kitopia.Desktop.Abstractions/` | BCL-only desktop contracts for platform identity, shell operations, file locks and selected text. |
| `src/Kitopia.Desktop.Features/` | Desktop feature implementations, view models, search/indexing, scenarios, plugin host, config, accounts, OCR and translation. |
| `src/Kitopia.Desktop/` | Desktop application composition, navigation, views/windows, UI presenters and platform integration. |
| `src/Kitopia.Desktop.Platform.Windows/` | Windows shell, application/Everything integration, hotkeys, file locks, selected text, native preview, capture backends and Explorer configuration. |
| `src/Kitopia.Desktop.Platform.Linux/` | Linux shell, app tools, clipboard, file lock and platform identity adapters. |
| `src/Kitopia.Desktop.PluginSdk/` | Public plugin contracts; builds as `PluginCore.dll` and retains `PluginCore.*` namespaces. |
| `src/Mobile/Kitopia.Mobile/` | Shared mobile application, configuration/persistence and platform adapters for chat attachments, clipboard, notifications and communication lifecycle. |
| `src/Mobile/Kitopia.Mobile.Android/` | Android entrypoint, permissions, native notifications and foreground service. |
| `src/Mobile/Kitopia.Mobile.iOS/` | iOS entrypoint and native runtime integration. |
| `plugins/KitopiaEx/` | Dynamically loaded desktop plugin with image/video compression, screenshot extensions, translation, analyzers, nodes and custom connectors. |
| `plugins/OnnxRuntime.CPU/`, `plugins/OnnxRuntime.Gpu.Win/`, `plugins/OnnxRuntime.OpenVino/` | ONNX inference backend plugins. Standalone plugin publication currently targets `win-x64`. |
| `samples/HasDependencyPluginBase/`, `samples/HasDependencyPlugin/` | Plugin dependency examples and verification. |
| `src/ContextMenuDll/` | Windows Explorer COM extension with Debug COM hosting and Release AOT behavior. |
| `samples/ContextMenuDllTester/` | Console harness for the Explorer extension. |
| `src/ContextMenu.Avalonia/`, `src/Kitopia.StoreCompanionPacking/` | Companion application and Desktop Bridge/MSIX packaging. |
| `external/NodifyM.Avalonia/` | Node editor library and example application. |
| `external/PinyinM.NET/` | Pinyin library and its test project. |
| `tests/KitopiaTest/` | Behavioral, UI, protocol, mobile and architecture tests, plus plugin lifecycle/unload fixtures. |
| `tests/KitopiaBenchmark/` | BenchmarkDotNet benchmarks. |
| `build/` | Fallout targets, FFmpeg bundling and installer input preparation. |
| `installer/ModernInstallerR/`, `installer/ModernInstallerR/modern_uninstaller_r/` | Rust installer and uninstaller crates; `scripts/build_release.ps1` produces installer artifacts. |
| `docs/` | Docsify documentation. `README.md` is the Chinese feature overview; `en-us/README.md` is the separate English version. |
