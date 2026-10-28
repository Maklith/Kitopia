# Kitopia agent instructions

## Essential rules

- 代码编写要求：不要添加无意义的函数、类、薄包装、拷贝或锁；能用框架自带类型和已有 API 实现时，直接使用，避免额外引入同步机制。
- 实现功能前先查找并复用已有的相同功能，禁止重复实现；必要时在对应主题文档记录复用入口与适用边界。
- Check applicable scoped instructions before editing: `AGENTS.md`, `AGENTS.override.md`, `.cursorrules`, `.cursor/rules/` and `.github/copilot-instructions.md`. Respect their scope and explicit user instructions.
- Preserve unrelated changes and submodule work. Keep edits focused; do not add global tooling or configuration without a task requirement.
- Use targeted builds and tests for routine verification. Fallout defaults, `LocalTest`, `Clean` and packaging targets can publish artifacts; read the verification guide before invoking them.

## Workflow

1. Understand the requested outcome; inspect `git status --short`, relevant code, callers and tests. Use `rg` for focused discovery.
2. Plan only as much as the task needs. Resolve routine choices from existing patterns; ask when missing information materially affects correctness or scope.
3. Implement through the owning layers, fix the cause and add useful regression coverage. Do not weaken tests or bypass architecture rules.
4. Run proportionate checks and inspect the final diff and `git diff --check`.
5. Have one reviewer sub-agent with fresh, task-focused context assess the result using the [review guide](docs/development/review.md). Resolve substantiated findings, rerun affected checks and have the same reviewer recheck fixes before delivery.
6. Report behavior, verification and review results, plus remaining limitations. Continue until complete or concretely blocked; involve the user when a requirement or behavior tradeoff needs their decision.

## Read only when needed

Paths below are relative to the repository root. Read the documents relevant to the task before editing or running commands; do not preload the whole set. Confirm implementation details in source. When behavior changes, update the owning document rather than expanding this file.

| When you need… | Read… |
| --- | --- |
| Project layout, entry points, ownership or submodules | [Repository map](docs/development/repository.md) |
| Product behavior, defaults or platform support | [Features](docs/development/features.md) |
| Layer dependencies, shared UI, platform boundaries or SDK contracts | [Architecture](docs/development/architecture.md) |
| Search, Everything, embeddings, index storage or retrieval benchmarks | [Search and indexing](docs/development/indexing.md) |
| ONNX backends, device probes, sessions or inference memory | [Inference](docs/development/inference.md) |
| Device discovery, chat, file transfers or compatibility | [Device communication](docs/development/device-communication.md) |
| Build/test commands, prerequisites, formatting or release side effects | [Verification](docs/development/verification.md) |
| Post-change independent review, unnecessary abstractions or review findings | [Review](docs/development/review.md) |
| Existing functionality, reuse, C#/Avalonia conventions, localization or test style | [Code conventions](docs/development/conventions.md) |
| Application data paths, documentation or Docsify routes | [Data and documentation](docs/development/data-and-docs.md) |
