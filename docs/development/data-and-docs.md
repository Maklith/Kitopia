# Local data and documentation

Paths in backticks are relative to the repository root unless stated otherwise.

- Desktop paths are centralized in `src/Kitopia.Desktop.Features/Utils/KitopiaPaths.cs`. Windows defaults to `%LOCALAPPDATA%\Kitopia`.
- Configurations, plugins, custom scenarios, logs, temporary files and received files have separate directories beneath the application root.
- Mobile persistence and attachment paths are owned by `src/Mobile/Kitopia.Mobile/Services/`; do not reuse desktop data paths.
- Keep `docs/README.md` claims aligned with registered features, platform support, plugin dependencies and default configuration.
- Preserve Docsify's `/en-us/` route and valid image paths under `docs/assets/README/`.
- When changing architecture or public contracts, update the relevant document under `docs/development/` instead of expanding a stale method-by-method catalog.

Index storage, model assets and retrieval benchmarks: [Search and indexing](indexing.md).
