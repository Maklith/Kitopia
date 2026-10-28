# Product features and support

Paths in backticks are relative to the repository root unless stated otherwise.

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
| KitopiaEx extensions | `plugins/KitopiaEx/`: screenshot OCR/QR/translation/pinning/saving, translation and window search analyzers, image/video compression, batch file renaming and scenario nodes. Search image actions (save, OCR, pin and compress) belong to KitopiaEx; the host only identifies clipboard images. Batch renaming previews conflicts before moving files; optional image compression creates renamed copies while preserving source images. Image/video compression and batch renaming register Explorer menu entries and SDK startup handlers on plugin enable and remove them on disable; the host routes selected paths after plugins are ready. |

### Defaults and Support Scope

- Default hotkeys are defined in `src/Kitopia.Desktop.Features/Services/Config/KitopiaConfig.cs`: search `Alt + Space`, screenshot `Ctrl + Alt + Q`, selected file preview `Space` in `explorer.exe`, selection translation `Ctrl + Alt + Y`, and window topmost `Ctrl + Alt + T`.
- Automatic selection translation uses a mouse drag-release trigger. Hotkey models support keyboard/mouse triggers and process scopes.
- The former mouse quick entry now presents file previews; do not describe it as a general mouse gesture system without checking the implementation.
- Windows is the primary desktop target. Linux has platform adapters but does not have every Windows feature.
- Android mobile focuses on device chat and file transfers. The Android project targets API 36 with minimum API 23 and produces APKs. iOS has a host project but is not packaged by the current release pipeline.
- OCR and semantic matching run locally with models and a usable ONNX backend. Translation and online markets use network services.
- Desktop translation defaults to the free Index-Translate online API (Index-Translate-35B-A3B); Microsoft Translator remains selectable through `KitopiaConfig.translationProvider`. Selection translation and KitopiaEx translation entries share the selected host service.
- Selection translation uses the host's `ISelectionTextTranslator` contract. Every Index-Translate selection uses one API request with the same dictionary/translation system instructions and completed examples localized to the target language. The model returns pronunciation, parts of speech, meanings and a bilingual example for words, fixed phrases, idioms and proper names; sentences, truncated clauses, lists and paragraphs receive only a translation. There is no separate classification request or length/word-count mode switch. Ordinary `PluginCore.ITranslationService` calls and Microsoft selection translation keep their normal translation response.
- Desktop update checks exclude prereleases by default. `KitopiaConfig.allowPrereleaseUpdates` enables them; both GitHub's prerelease flag and the NuGet version label are checked, and draft releases are always excluded.

Discovery protocol and compatibility details: [Device communication](device-communication.md).
