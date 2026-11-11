# Build and verification

Paths in backticks are relative to the repository root unless stated otherwise.

Run commands from the repository root unless stated otherwise. The root `global.json` selects the latest installed stable .NET 10 SDK feature band; use the same SDK in the IDE and CLI so incremental builds agree on generated runtime configuration files. Android requires the Android workload and SDK; iOS requires the corresponding workload, macOS and Xcode. Full solution builds include mobile and Windows packaging projects, so choose a project for routine checks.

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

Windows desktop Debug builds keep optional large assets in separate plugins. The `KitopiaFfmpeg` plugin project prepares its LGPL bundle through `BundleFfmpeg`; the first build can download dependencies. The `KitopiaSemanticSearch` plugin project publishes EmbeddingGemma 2, including its native ONNX external-data shards. The client continues to copy OCR assets into local application data. EmbeddingGemma 2 shards remain below GitHub's 100 MiB file limit, with one intact 64 MiB embedding tensor. Git LFS and runtime merging are not required.

Compression tests resolve the prepared FFmpeg bundle from `artifacts/ffmpeg/<rid>/tools/ffmpeg` through `tests/KitopiaTest/Services/FfmpegTestAssets.cs`. Prepare that bundle explicitly before running native compression tests; client and test builds do not build the optional asset plugins automatically.

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

Use `./build/build.cmd --help` to inspect targets. Shell equivalents are `./build/build.ps1` and `./build/build.sh`.

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
| `PackFfmpegPlugin`, `PackSemanticSearchPlugin`, `PackAssetPlugins` | Publishes FFmpeg and semantic-search ZIPs separately to the plugin API; requires their own API keys. |
| `LocalTest` | Publishes Windows x64, publishes ONNX and asset plugins and builds an installer. This is not a unit-test target. |
| `Clean` | Full Windows, ONNX/asset plugin, Android and installer packaging/publishing dependency chain. This is not a cleanup command. |
| `CompileWindowsX64`, `BuildNativeInstaller` | Compatibility aliases for `PackWindowsX64` and `PackInstallerX64`; not standalone local compile commands. |

Package targets depend on `CreateRelease`, which requires `GitHubToken` and creates a public release/tag when the version does not already exist. The release is marked as a prerelease only when its NuGet version has a prerelease label, such as `-beta.1` or `-rc.1`. An existing tag causes those release package targets to be skipped.

The default entrypoint selects `Clean` when a GitHub token is supplied and `LocalTest` otherwise. Standalone plugin publication uses configured API keys and defaults to a local API for local runs. Treat default/package targets as publishing workflows; use `dotnet build` and `dotnet test` for ordinary verification.

### Asset plugin publication

`Clean` and `LocalTest` publish `kitopiaffmpeg` and `kitopiasemanticsearch` independently through the same version check, archive creation and upload API as ONNX plugins. `PackAssetPlugins` publishes both; `PackFfmpegPlugin` and `PackSemanticSearchPlugin` publish just one. Already-published versions are skipped; bump each plugin project and manifest together to release an update. These versions are independent of the desktop version.

GitHub Actions reads `PLUGINAPIKEYFFMPEG` and `PLUGINAPIKEYSEMANTICSEARCH` secrets into `PluginApiKeyFfmpeg` and `PluginApiKeySemanticSearch`. Register both plugins on the server before publishing. Server upload/storage quotas remain an administrator setting, including the larger model package and retained revisions.

Windows desktop ZIPs and installers include KitopiaEx and the CPU backend; FFmpeg and semantic models are installed from the plugin market. The market exposes one artifact per OS, so each asset plugin publishes a single `win-universal` archive with an AnyCPU entry assembly. FFmpeg carries both `tools/ffmpeg/win-x64` and `tools/ffmpeg/win-arm64`; its entry point selects by process architecture, with support for the previous flat Debug layout. The semantic model files are shared across architectures. `Build.PublishStandalonePlugin` is the shared publication entry point; `Build.AssetPlugins.cs` supplies FFmpeg bundle preparation without duplicating upload logic.

### Companion Store package version

`src/Kitopia.StoreCompanionPacking/Kitopia.StoreCompanionPacking.wapproj` selects `Package.debug.appxmanifest` for Debug and `Package.appxmanifest` for Release and other configurations. Set `Identity/@Version` in the release manifest when publishing a Companion update; editing only the debug manifest does not change the Store package version. Use the four-part MSIX version (for example `1.2.0.0`). This version is independent of the desktop client's `KitopiaVersion.props`; `AppxAutoIncrementPackageRevision` is currently disabled.

Each configuration must evaluate to exactly one `AppxManifest` item. Do not add an unconditional manifest include alongside the existing conditional entries: signing tasks such as `GenerateDisposableSigningCertificate` accept a single manifest, and duplicate entries fail even when they name the same file.

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
