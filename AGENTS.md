# AGENTS Guide for Kitopia

This guide describes the current repository layout, feature ownership, and verification workflow.

## 1. Working Rules

- 代码编写要求：不要添加无意义的函数、类、薄包装、拷贝或锁；能用框架自带类型和已有 API 实现时，直接使用，避免额外引入同步机制。
- Follow the local style and keep changes focused on the requested behavior.
- Read the relevant implementation and tests before editing. Preserve unrelated work, including changes inside submodules.
- Introduce abstractions only when they remove meaningful complexity or express an actual contract.
- Use targeted builds and tests before considering the full solution or packaging pipeline.
- Do not introduce global tooling or configuration files without a task requirement.
- Check for additional scoped instructions in the area being changed. Honor applicable `AGENTS.md`, `.cursorrules`, `.cursor/rules/`, and `.github/copilot-instructions.md` when present.

## 2. Stack and Entry Points

- C#/.NET 10, Avalonia, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, and Serilog.
- Source projects live in `src/`, plugins in `plugins/`, tests and benchmarks in `tests/`, examples in `samples/`, third-party libraries in `external/`, and the Rust installer in `installer/`.
- Versions shared by desktop projects are defined in `KitopiaVersion.props` and imported through the root `Directory.Build.props`; plugin versions and manifests are maintained by their own projects.
- Solution files: `Kitopia.slnx` and `Kitopia.sln`. Architecture tests currently locate the repository using `Kitopia.sln`.
- Desktop composition root: `src/Kitopia.Desktop/Program.cs`; Avalonia lifecycle: `src/Kitopia.Desktop/App.axaml.cs`.
- Mobile composition: `src/Mobile/Kitopia.Mobile/App.axaml.cs` and `src/Mobile/Kitopia.Mobile/Services/`; native lifecycle code belongs to the Android/iOS hosts.
- Build automation: Fallout (`FalloutBuild`), implemented in `build/Build.cs` and `build/Build.*.cs`, launched by `build.cmd`, `build.ps1`, or `build.sh`.
- Main tests: `tests/KitopiaTest/KitopiaTest.csproj`, MSTest with Avalonia.Headless for UI tests.
- Windows installer and uninstaller: separate Rust crates under `installer/ModernInstallerR/`.

### Submodules

The following directories are Git submodules, not ordinary source folders:

- `src/Kitopia.Desktop.PluginSdk/`
- `external/NodifyM.Avalonia/`
- `external/PinyinM.NET/`
- `installer/ModernInstallerR/`

Initialize missing submodules with `git submodule update --init --recursive`. Inspect their working trees before edits; preserve local changes and avoid accidental submodule pointer updates.

## 3. Current Product Features

The desktop feature catalog is in `src/Kitopia.Desktop.Features/Services/Plugin/KitopiaFeatures.cs`. The main navigation items are in `src/Kitopia.Desktop.Features/ViewModel/Main/MainWindowViewModel.cs`; `src/Kitopia.Desktop/Program.cs` registers the corresponding views.

| Capability | Main implementation |
| --- | --- |
| Search and launcher | `src/Kitopia.Desktop.Features/Search/`: app/file/scenario search, pinyin, ranking, history, pins, input recognition, result actions and previews. |
| Local semantic search and indexing | `src/Kitopia.Desktop.Features/Indexing/` and `Search/Semantic/`: EmbeddingGemma 2 ONNX Q4 text/image embeddings (float32 activations, shared 768 dimensions), document extraction, SQLite/sqlite-vec storage, maintenance and status. |
| File preview | `Search/Preview/`, `Search/ViewModels/MouseQuickWindowViewModel.cs`, desktop `Controls/FilePreviewControl.axaml`, and Windows `NativeFilePreviewHost.cs`. |
| Screenshots | Desktop `Windows/ScreenCaptureWindow.axaml`, `Controls/Capture/`, long screenshot UI, and Windows `ScreenCapture/`: selection, HDR capture, annotation, color picking and stitching. |
| OCR and translation | Desktop features `Ocr/` and `Translation/`; Windows selection access in the platform project and selection translation UI/service in the desktop host. |
| Device chat and file sharing | `src/Kitopia.Feature/` owns discovery, identity, protocol, TCP/TLS, messages and transfers; `src/Kitopia.Feature.Avalonia/` owns reusable chat UI. |
| Custom scenarios | `src/Kitopia.Desktop.Features/CustomScenario/`: graph, execution, persistence, editor view models, triggers and scenario market. Desktop `Windows/TaskEditor/` owns the editor window. |
| Plugin host and market | `src/Kitopia.Desktop.Features/PluginHost/`: discovery, dependencies, loading/unloading, packages, downloads, market and plugin settings. |
| Models and system tools | `Services/Onnx/`, model manager, hotkey manager, file lock UI, LAN share window, shell integration, settings and account services. |
| KitopiaEx extensions | `plugins/KitopiaEx/`: screenshot OCR/QR/translation/pinning/saving, translation and window search analyzers, image/video compression and scenario nodes. |

### Defaults and Support Scope

- Default hotkeys are defined in `src/Kitopia.Desktop.Features/Services/Config/KitopiaConfig.cs`: search `Alt + Space`, screenshot `Ctrl + Alt + Q`, selected file preview `Space` in `explorer.exe`, selection translation `Ctrl + Alt + Y`, and window topmost `Ctrl + Alt + T`.
- Automatic selection translation uses a mouse drag-release trigger. Hotkey models support keyboard/mouse triggers and process scopes.
- The former mouse quick entry now presents file previews; do not describe it as a general mouse gesture system without checking the implementation.
- Windows is the primary desktop target. Linux has platform adapters but does not have every Windows feature.
- Android mobile focuses on device chat and file transfers. The Android project targets API 36 with minimum API 23 and produces APKs. iOS has a host project but is not packaged by the current release pipeline.
- OCR and semantic matching run locally with models and a usable ONNX backend. Translation and online markets use network services.
- Desktop update checks exclude prereleases by default. `KitopiaConfig.allowPrereleaseUpdates` enables them; both GitHub's prerelease flag and the NuGet version label are checked, and draft releases are always excluded.
- Device discovery uses IPv4/IPv6 UDP multicast on port `49526`. Data transport currently supports TCP with TLS and device public-key certificate validation; the current transport enum does not expose QUIC.
- Device communication is independent of account login. Clipboard sending is handled by the chat UI and platform contracts; do not assume the removed clipboard message API still exists.

## 4. Projects and Responsibilities

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

## 5. Architecture Boundaries

These rules are enforced by `tests/KitopiaTest/Architecture/`, especially `ProjectDependencyBoundaryTests.cs` and `LayeringConventionTests.cs`.

- `Kitopia.Feature` must remain independent of Avalonia, desktop/mobile hosts, native APIs, OS branches and platform compilation symbols.
- `Kitopia.Feature.Avalonia` references `Kitopia.Feature` only as a project dependency. Keep native APIs, OS branches and host types out of this reusable UI layer.
- Mobile projects must not reference desktop projects or `PluginCore`, including transitively or through linked source/AXAML.
- `Kitopia.Desktop.Abstractions` contains real BCL-only contracts, with no package or project references.
- The plugin SDK must not depend on a host, desktop abstractions or platform implementation. Preserve public names, signatures and the `PluginCore` assembly identity.
- Desktop features must not depend on Windows/Linux adapters or mobile hosts. The approved project references are desktop abstractions, the plugin SDK, shared feature/UI projects, NodifyM and Pinyin.
- Windows and Linux adapters must not depend on each other or mobile hosts. The desktop composition root must not depend on a mobile host.
- Inject platform behavior through the owning contracts and register implementations in the host. Keep reusable logic in its existing feature layer.

### Key Contracts

Consult the source for exact signatures instead of maintaining a duplicate method catalog.

- Desktop lifecycle/config/navigation/account/UI contracts: `src/Kitopia.Desktop.Features/Services/Interfaces/`, including `IApplicationService`, `IConfigService`, `INavigationService`, `IAccountService`, and `IHotKetImpl`.
- `IConfigService.Version` and `ConfigManger.Version` use `NuGet.Versioning.NuGetVersion`, preserving prerelease labels for host dependency checks. Desktop update comparisons use semantic version precedence and ignore build metadata.
- Indexing/search: `Indexing/IIndexService.cs` also defines `ISearchEntryIndex` and `IIndexMaintenanceService`; app indexing and Everything contracts are in `Search/`.
- Document text vectors are stored by file key and chunk index in `IndexVectorStore`. Images use EG2 image embeddings without OCR; old OCR text vectors are removed when the index database opens while image/document vectors, file sources and fingerprints survive. PP-OCRv6 small remains available for screenshots and explicit OCR operations. Search groups document chunk hits by file using the highest similarity; replacement, copying and deletion must include every chunk. Normal indexing uses at most 1024 tokens per chunk, including title/prompt/special tokens, with 48 tokens of content overlap. Text batches contain at most four items and a quadratic attention budget allows only one 1024-token item at a time. Normal text inputs are padded to power-of-two lengths up to 1024, with masks excluding padding, to reuse a small set of arena shapes. The model still accepts up to 8192 tokens for explicit calls. The model ID includes the chunking policy so old vectors are replaced during incremental indexing while file sources and fingerprints are preserved.
- Native desktop operations: `IDesktopPlatformInfo`, `Shell/IDesktopShell`, `FileSystem/IFileLockService`, and `TextSelection/ITextSelectionService` in `src/Kitopia.Desktop.Abstractions/`.
- Shared communication contracts: `src/Kitopia.Feature/Application/`, `Discovery/`, `Transport/`, `Identity/`, `Security/`, `Sessions/`, and `DeviceCommunicationRuntime.cs`.
- File transfer UI uses `FileTransferStatus`; local accept/reject/cancel actions publish the same events as remote updates. `FileTransferUpdatedEvent.LocalFilePath` carries an optional local save path for completed-file actions.
- Chat platform integration: `IChatPlatformService`, `IChatClipboardService`, `IChatAttachmentStore`, and `IChatNotificationSink` live in `src/Kitopia.Feature/Application/`.
- Scenario editor/upload contracts: `src/Kitopia.Desktop.Features/CustomScenario/Services/`; plugin operations: `PluginHost/Contracts/`.
- Public extension points: `IPlugin`, `IPluginManger`, clipboard, screenshot, OCR, translation, search, toast, inference and scenario contracts in `src/Kitopia.Desktop.PluginSdk/`.
- Mixed int64/float32 inference with named outputs is supported by the ONNX backend plugins for EmbeddingGemma 2; older backends report unsupported inference through the SDK's default method.
- OpenVINO uses Intel's standalone `Intel.ML.OnnxRuntime.EP.OpenVINO` package with standard `Microsoft.ML.OnnxRuntime`. Its plugin singleton registers the EP library lazily, selects reported devices by hardware type, and unregisters after the container disposes inference sessions. Sessions and availability probes enable `session.use_env_allocators=1` to use the device allocators created by EP registration; those shared allocators are released when the EP is unregistered. CPU precision and worker limits use OpenVINO's `load_config` with `INFERENCE_PRECISION_HINT=f32`, `EXECUTION_MODE_HINT=ACCURACY`, and `INFERENCE_NUM_THREADS`. NPU detection and inference use `load_config` with `NPU_COMPILER_TYPE=DRIVER` for on-device compilation through the installed Intel NPU driver; `session.disable_cpu_ep_fallback=1` also prevents the OpenVINO EP from retrying NPU compilation on its CPU backend. Do not hardcode a different hardware platform. ORT graph optimization is disabled as officially recommended. Keep original ONNX tensor names and external-data shards unchanged.
- On Windows, the OpenVINO singleton retains a native reference to the system `ze_loader.dll` before registering its EP library. Release it only after unregistering the EP, so temporary device enumeration cannot tear down the Level Zero driver before session creation. This reference does not retain model sessions or inference buffers.
- Inference worker limits use the SDK's optional session initialization overload, never process-wide CPU affinity. Manual indexing pause releases embedding sessions after in-flight inference exits and does not affect independent OCR sessions. Embedding batches yield their shared gate between runs and account for quadratic attention allocations. Embedding/OCR reuse session-local arenas during a pass, with memory patterns disabled for dynamic shapes. CUDA uses exact arena growth and a configurable per-session GPU arena limit (8192 MiB by default, allocated on demand); buffers are released with sessions on pause/completion rather than after each inference. This limit excludes driver/workspace memory and is not a process-wide RAM cap. CUDA file-model initialization and mixed-input allocation failures use the BCL OutOfMemoryException contract. EmbeddingGemma releases CUDA text/vision caches and retries text inference once with a fresh session; if allocation still fails and the CPU plugin is installed, it uses CPU until sessions are released or the selected backend changes. Recovery preserves input tokens and image features, respects cancellation, and never changes the persisted backend choice.
- `IInferenceSession.CheckAvailability` is an optional device probe: older plugins return unknown. Official backends resolve their bundled native libraries and run a small MatMul model, with CPU fallback disabled for CUDA/OpenVINO. CUDA also preloads the CUDA 13 build of cuDNN 9 from `CUDNN_PATH`, `PATH`, or NVIDIA's standard installation directory, then executes BiasSoftmax to verify cuDNN. The model manager uses `IOnnxRuntimeProbe` to run each device check in a short-lived desktop child process (`--onnx-runtime-probe`), before UI, single-instance communication or background services initialize. The parent subscribes to a unique `kitopia/onnx-probe/<request-id>` topic on its existing local MQTT broker before starting the child; JSON payloads return availability or diagnostic errors without result files. Cancellation and a 60-second timeout terminate the child. Completed availability/error results are cached only in memory per enabled plugin instance and device, using weak plugin keys so unloading is not blocked. Page attachment reuses results; the recheck button forces fresh probes. Reloading/updating a plugin or its dependencies creates a new plugin instance and invalidates its cached results; cancellation is not cached. Native crashes mark only that backend unavailable, and probe libraries/driver state are released when the child exits. The page shows requirements and official links and blocks selection of confirmed unavailable devices. A passing environment probe does not guarantee compatibility with every model.
- Explorer COM contracts: `src/ContextMenuDll/Interop/ComInterfaces.cs`.

## 6. Build and Verification

Run commands from the repository root unless stated otherwise. Use .NET 10 SDK. Android requires the Android workload and SDK; iOS requires the corresponding workload, macOS and Xcode. Full solution builds include mobile and Windows packaging projects, so choose a project for routine checks.

### Targeted .NET Builds

```powershell
dotnet restore "src/Kitopia.Desktop/Kitopia.Desktop.csproj"
dotnet build "src/Kitopia.Desktop/Kitopia.Desktop.csproj" -c Debug
dotnet build "src/Kitopia.Desktop.Features/Kitopia.Desktop.Features.csproj" -c Debug -f net10.0
dotnet build "src/Kitopia.Feature/Kitopia.Feature.csproj" -c Debug
dotnet build "src/Kitopia.Feature.Avalonia/Kitopia.Feature.Avalonia.csproj" -c Debug
dotnet build "src/Mobile/Kitopia.Mobile/Kitopia.Mobile.csproj" -c Debug
dotnet build "src/Mobile/Kitopia.Mobile.Android/Kitopia.Mobile.Android.csproj" -c Debug -r android-arm64
```

Windows desktop Debug builds automatically prepare the FFmpeg bundle through `BundleFfmpeg`; the first build can download dependencies. See `build/Ffmpeg/README.md`. Debug builds also copy EmbeddingGemma 2 and OCR assets into the local application data directory. EmbeddingGemma 2 uses native ONNX external-data shards up to 32 MiB, except one intact 64 MiB embedding tensor. Git LFS and runtime merging are not required.

When all platform prerequisites are available:

```powershell
dotnet restore "Kitopia.slnx"
dotnet build "Kitopia.slnx" -c Debug
```

### MSTest

```powershell
dotnet test "tests/KitopiaTest/KitopiaTest.csproj"
dotnet test "tests/KitopiaTest/KitopiaTest.csproj" --list-tests
dotnet test "tests/KitopiaTest/KitopiaTest.csproj" --filter "FullyQualifiedName~KitopiaTest.DeviceCommunication.SharedProtocolFrameTests"
dotnet test "tests/KitopiaTest/KitopiaTest.csproj" --filter "FullyQualifiedName~KitopiaTest.DeviceCommunication.SharedProtocolFrameTests.BuildHeader_ThenReadHeader_RoundTrips"
dotnet test "tests/KitopiaTest/KitopiaTest.csproj" --filter "FullyQualifiedName~KitopiaTest.Architecture"
```

- Single-test runs still build a large dependency graph.
- Relevant test areas include `Search/`, `Indexing/`, `CustomScenario/`, `DeviceCommunication/`, `src/Mobile/`, `Services/` and `Architecture/`.
- Run architecture tests when changing project references, feature ownership, shared UI or SDK contracts.
- Scale behavioral coverage to the risk. Documentation-only edits need link/path/content checks rather than a full application build.
- Existing build warnings are not globally treated as errors.

### Format and Analysis

```powershell
dotnet format "Kitopia.slnx" --verify-no-changes
```

Use `--include` to limit formatting to touched files when applying changes. There is no dedicated format target in the Fallout pipeline.

### Fallout Targets and Side Effects

Use `./build.cmd --help` to inspect targets. Shell equivalents are `./build.ps1` and `./build.sh`.

| Target | Behavior |
| --- | --- |
| `RestoreWindows` | Initializes submodules and restores desktop/plugin projects. |
| `RestoreAndroid` | Restores the Android host. |
| `Restore` | Runs both restore targets; requires mobile prerequisites. |
| `BundleFfmpeg --ffmpeg-runtime win-x64` | Prepares the Windows FFmpeg bundle; `win-arm64` is also supported. |
| `PackWindowsX64`, `PackWindowsArm64`, `PackWindows` | Publishes desktop/plugin output, creates ZIPs and uploads release assets. |
| `PackAndroidX64`, `PackAndroidArm64`, `PackAndroid` | Publishes APKs and uploads release assets. |
| `PackInstallerX64`, `PackInstallerArm64`, `PackInstaller` | Prepares archives, invokes the Rust release script and uploads installers. |
| `PackOnnxRuntimeCpu`, `PackOnnxRuntimeGpuWin`, `PackOnnxRuntimeOpenVino`, `PackOnnxPlugins` | Publishes ONNX plugin packages to the configured plugin API; requires API keys. |
| `LocalTest` | Publishes Windows x64, publishes ONNX plugins and builds an installer. This is not a unit-test target. |
| `Clean` | Full Windows, ONNX plugin, Android and installer packaging/publishing dependency chain. This is not a cleanup command. |
| `CompileWindowsX64`, `BuildNativeInstaller` | Compatibility aliases for `PackWindowsX64` and `PackInstallerX64`; not standalone local compile commands. |

Package targets depend on `CreateRelease`, which requires `GitHubToken` and creates a public release/tag when the version does not already exist. The release is marked as a prerelease only when its NuGet version has a prerelease label, such as `-beta.1` or `-rc.1`. An existing tag causes those release package targets to be skipped.

The default entrypoint selects `Clean` when a GitHub token is supplied and `LocalTest` otherwise. ONNX publication uses configured API keys and defaults to a local API for local runs. Treat default/package targets as publishing workflows; use `dotnet build` and `dotnet test` for ordinary verification.

### Rust Installer

Run inside `installer/ModernInstallerR/`:

```powershell
cargo build --bin modern_installer_r
cargo build --release --bin modern_installer_r
cargo test
cargo fmt --all -- --check
cargo clippy --all-targets --all-features -- -D warnings
```

The uninstaller is a separate crate. Check it from its own directory or use `--manifest-path modern_uninstaller_r/Cargo.toml`. For prepared installer inputs, `scripts/build_release.ps1 -Target x86_64-pc-windows-msvc` also supports `aarch64-pc-windows-msvc`; consult the script before changing packaging.

## 7. Code Conventions

- Follow existing namespace/style conventions; use file-scoped namespaces where established.
- Prefer one top-level type per file and `sealed` for concrete classes that are not designed for inheritance.
- Use records/record structs for immutable messages when they fit the surrounding API.
- Keep usings at the top, grouped as `System.*`, third-party, then project namespaces. Remove unused imports in touched files.
- Types, methods, properties and events use PascalCase; locals/parameters use camelCase; private fields use `_camelCase`.
- Honor nullable annotations and validate boundary inputs with specific exceptions.
- Propagate cancellation tokens and avoid `.Result`/`.Wait()` in runtime async code. Use `ValueTask` when the existing contract uses it.
- Prefer structured Serilog logging through the area's existing logger pattern; avoid noisy logging in hot paths.
- Avalonia compiled bindings are enabled in the UI projects. Supply `x:DataType` and keep rendering in views, state/behavior in view models.
- Use CommunityToolkit `[ObservableProperty]` and `[RelayCommand]` where the area already does.
- Put user-facing strings in the owning language resources: shared `src/Kitopia.Feature/lang/` or plugin `lang/`. Follow `docs/i18n.md` when editing localization.
- MSTest uses `[TestClass]` and `[TestMethod]`; prefer `Method_Condition_Expected` names and deterministic, isolated tests.
- Add brief comments only where they explain a non-obvious constraint or algorithm.

## 8. Local Data and Documentation

- Desktop paths are centralized in `src/Kitopia.Desktop.Features/Utils/KitopiaPaths.cs`. Windows defaults to `%LOCALAPPDATA%\Kitopia`.
- Configurations, plugins, custom scenarios, logs, temporary files and received files have separate directories beneath the application root.
- The current vector/index database is `index.db` under that root. Do not reintroduce the legacy `search-rag.db` path into the current index store.
- EmbeddingGemma 2 Q4 assets live under `EmbeddingGemma2`. Queries use `task: search result | query: `; documents use `title: {title or none} | text: {content}`; images have no text prompt. Older vector dimensions are invalidated while managed file sources and file fingerprints remain for incremental rebuilding.
- Mobile persistence and attachment paths are owned by `src/Mobile/Kitopia.Mobile/Services/`; do not reuse desktop data paths.
- Keep `docs/README.md` claims aligned with registered features, platform support, plugin dependencies and default configuration.
- Preserve Docsify's `/en-us/` route and valid image paths under `docs/assets/README/`.
- Image retrieval benchmarks reuse the README feature images under `docs/assets/README/`, selected by the screenshot annotations in `tests/KitopiaBenchmark/EmbeddingComparison/image-queries*.json`.
- When changing architecture or public contracts, update this guide's ownership/boundary notes instead of expanding a stale method-by-method catalog.
