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
