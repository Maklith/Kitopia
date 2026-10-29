# Independent review before delivery

After implementation and targeted verification, use one reviewer sub-agent to assess the result against the user's requirements before delivery. The implementing agent owns corrections and verification; the reviewer supplies an independent assessment. User involvement is reserved for decisions that require their judgment.

## 1. Prepare the evidence

- Define observable acceptance criteria from the request before implementation. For a bug, capture a reproduction or failing regression check when feasible. A short task description is enough for a small edit; do not create a separate specification file merely to satisfy this process.
- Run the smallest useful checks from the [verification guide](verification.md). Retain commands, outcomes and relevant output, including failed or unavailable checks. For visible behavior, exercise the affected interaction or inspect its rendered result when the environment permits; disclose when only automated checks were possible.
- Identify the current task's changed files and diff, including relevant untracked files. Separate pre-existing or unrelated work and identify the version of the changes the evidence covers. Do not commit or stash user work just to establish a review baseline.
- Scale the review to the change. Documentation-only changes need accuracy, scope and link checks; they do not require application builds or invented functional tests.

## 2. Request an independent review

- Start one reviewer with fresh, task-focused context. With Codex's agent tool, use `fork_turns: "none"` when available; supply the request, acceptance criteria, changed paths/diff, applicable instructions and verification evidence explicitly. Avoid passing the whole implementation conversation or arguments for why the solution must be correct.
- The reviewer reads the actual diff, relevant implementation, callers, tests and task-specific guides. The implementing agent's summary is a starting point, not evidence of correctness. Read related unchanged code to assess the change, but keep findings tied to the task's changes or acceptance criteria.
- Keep the review read-only: no file edits, mutating commands, publishing or further delegation. Use a restricted tool set where the harness supports it. In Codex, a read-only instruction does not create a separate sandbox; sub-agents inherit the parent's permissions.
- Wait for the review before delivery and keep the reviewed changes stable while it runs. If the implementation changes, identify the delta and provide updated evidence before asking for the verdict. Return concise findings rather than raw exploration logs.

## 3. Review against the requirements

- **Functional fit:** Does the change fulfill the requested behavior across affected paths? Check edge cases, cancellation, errors, resource ownership and compatibility where relevant. Passing tests alone do not establish that the intended feature works.
- **Existing functionality and duplication:** Check for an existing implementation of the changed behavior in the owning feature, shared contracts or framework APIs. For a duplicate, identify both implementations and a direct reuse path that preserves behavior and architecture boundaries. Check that necessary reuse entry points or constraints are recorded in the corresponding topic document, following the [reuse conventions](conventions.md#reuse-existing-functionality). Judge equivalent behavior and real constraints, not textual similarity alone.
- **Unnecessary indirection:** Look for functions, classes, interfaces or services that only forward calls, rename existing APIs or duplicate an existing responsibility without an actual contract or reduction in complexity. For a proposed simplification, identify the direct existing API or code path and explain the layer's concrete cost and why removing it preserves behavior.
- **Unnecessary work or synchronization:** Check copies, materialization, intermediate state, allocations and locks against actual ownership and concurrency requirements. Do not introduce a custom synchronization mechanism when an existing type or API already provides the required guarantees.
- **Boundaries and scope:** Check feature ownership, public contracts and unrelated refactoring against the [architecture guide](architecture.md) and the user's constraints.
- **Verification quality:** Check that tests and commands exercise the changed behavior, including regressions where useful. Flag hidden failures, weakened assertions and verification that was claimed but not run.

A short method or adapter may serve boundary validation, compatibility, resource lifetime or a real platform contract. Evaluate its responsibility and call sites. Skip stylistic preferences, speculative extensions and demands for abstractions or tests for impossible cases. Leave formatting and other mechanical checks to the existing tools; report actionable findings rather than a catalogue of inspected types.

## 4. Report, correct and recheck

- For each finding, provide severity, file and line, a concrete trigger or evidence, the functional or maintenance cost and the smallest reasonable correction. Distinguish demonstrated problems from questions. An empty findings list is valid; do not invent work to justify the review.
- Give a concise outcome: **Ready** when the acceptance criteria have adequate evidence and no substantiated issue remains; **Needs changes** when an issue requires correction; **Unable to verify** when essential evidence is unavailable. State what was checked and any limitations. A clean review does not replace tests or establish untested behavior.
- The implementing agent validates findings, fixes substantiated issues within scope and reruns affected checks. Reuse the same reviewer to confirm the corrections and any behavior they introduce; do not restart an unrestricted search for new preferences after each fix. Explain with evidence when a finding is rejected.
- Deliver once substantiated issues within scope are resolved and verification is adequate. If a requirement conflict, behavior tradeoff or concrete blocker remains, surface it explicitly and ask the user only for the decision needed to proceed. Report unresolved verification gaps rather than describing them as a pass.
- If sub-agents are unavailable, perform and report a self-review using these checks and disclose that independent review could not be run. Never describe self-review as independent review.

After the final review, the reviewer returns a concise plain-text report to the implementing agent containing:

- Scope reviewed and acceptance criteria.
- Final verdict and actionable findings, or an explicit statement that no actionable findings remain.
- The disposition of each finding: fixed and rechecked, rejected with evidence, or unresolved for the user.
- Verification actually run, failed or unavailable checks, and material limitations.

The implementing agent presents this report, along with a brief description of the delivered behavior, in the final response after the reviewer has finished. Keep it readable and self-contained. Do not create a per-task report file unless the user requests a file; do not claim independent review when only self-review was possible.

## Public guidance used

| Source | Practice adopted here |
| --- | --- |
| [OpenAI: Subagents](https://developers.openai.com/codex/subagents/) | Bounded tasks, explicit handoffs, concise returned findings and care around shared writes; sub-agents inherit the parent's permissions. |
| [OpenAI: Code review](https://developers.openai.com/codex/cloud/code-review/) | Consequential repository-specific rules, clear exceptions and separation of review judgment from mechanical checks. |
| [Anthropic: Best practices](https://code.claude.com/docs/en/best-practices) | Runnable verification, fresh-context review against the specification and skipping stylistic findings that create unnecessary work. |
| [Anthropic: Sub-agents](https://code.claude.com/docs/en/sub-agents#code-reviewer) | A focused reviewer that reads the diff, uses read-only tools and returns prioritized findings with concrete corrections. |

This SOP adapts public product guidance. One reviewer, the thin-wrapper criteria and the recheck procedure are Kitopia choices. `AGENTS.md` directs model behavior; deterministic enforcement belongs in tests, CI or harness hooks when required by the task.
