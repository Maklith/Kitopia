# Code conventions

Paths in backticks are relative to the repository root unless stated otherwise.

## Reuse existing functionality

- Before adding behavior, search the owning feature, shared contracts, existing helpers and framework APIs for equivalent functionality. Read their behavior and call sites; reuse the existing implementation instead of creating a second version under a different name or in another directory.
- When multiple callers need the same behavior, extend its owning implementation or move genuinely shared logic to an allowed common layer only when this removes meaningful duplication. Preserve architecture boundaries and public contracts; do not add forwarding wrappers or unrelated refactoring merely to claim reuse.
- When a reuse entry point is difficult to discover or its limits matter to future callers, document its source path or contract, purpose and applicable boundaries in the corresponding topic document under `docs/development/`. If separate implementations are required by actual platform or compatibility differences, record that constraint there. Link to source instead of copying code or maintaining a method-by-method catalogue.

## C# and Avalonia style

- Follow existing namespace/style conventions; use file-scoped namespaces where established.
- Prefer one top-level type per file and `sealed` for concrete classes that are not designed for inheritance.
- Use records/record structs for immutable messages when they fit the surrounding API.
- Keep usings at the top, grouped as `System.*`, third-party, then project namespaces. Remove unused imports in touched files.
- Types, methods, properties and events use PascalCase; locals/parameters use camelCase; private fields use `_camelCase`.
- Honor nullable annotations and validate boundary inputs with specific exceptions.
- Propagate cancellation tokens and avoid `.Result`/`.Wait()` in runtime async code. Use `ValueTask` when the existing contract uses it.
- Prefer structured Serilog logging through the area's existing logger pattern; avoid noisy logging in hot paths.
- Avalonia compiled bindings are enabled in the UI projects. Supply `x:DataType` and keep rendering in views, state/behavior in view models.
- Single-directory settings use the SDK's `ConfigFieldType.目录` on a string field. The desktop settings page displays the current path beside a folder picker and persists the selected directory.
- Use CommunityToolkit `[ObservableProperty]` and `[RelayCommand]` where the area already does.
- Put user-facing strings in the owning language resources: shared `src/Kitopia.Feature/lang/` or plugin `lang/`. Follow the [localization guide](../i18n.md) when editing localization.
- MSTest uses `[TestClass]` and `[TestMethod]`; prefer `Method_Condition_Expected` names and deterministic, isolated tests.
- Add brief comments only where they explain a non-obvious constraint or algorithm.
