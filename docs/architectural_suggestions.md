# WheelHouse Suggestions and Architectural Improvements

This document tracks the evolution of WheelHouse's core orchestration design. The original five
proposals have all shipped; they're recorded below for history, followed by the current backlog of
refinements that the codebase now actually needs.

---

## Shipped (originally proposed, now implemented)

| # | Proposal | Status | Landed in |
|---|----------|--------|-----------|
| 1 | RAG-powered research planning | ✅ Shipped | `SessionView.BuildPlanningContext()` injects `IVectorSearchService` hits plus `.wheelhouse/knowledge.md` into the plan prompt. |
| 2 | Multi-language AST-lite compression | ✅ Shipped (exceeded) | `CodeCompressionService` handles ~20 C-style extensions plus hash-comment languages (Python/Ruby/shell), not just `.cs`. |
| 3 | Session transcript export | ✅ Shipped | `SessionExport` / `SessionArchive` + "Export MD" and "Save to workspace" buttons write `.wheelhouse/sessions/<id>.md`. |
| 4 | Git branch isolation & safe rollbacks | ✅ Shipped | `GitService` + the session git bar: "Branch for this session" and "Discard changes". |
| 5 | Visual pipeline UI canvas | ✅ Shipped (exceeded) | `ScriptGraphView` renders a runnable node graph; `PipelineStages()` is the linear fallback. |

The detail of the original proposals is preserved in version control history.

---

## Active backlog

### P1 — Correctness

#### 1.1 Stderr drain deadlock in the Claude subprocess — ✅ Fixed
`ClaudeCliService.RunAsync` previously read stdout to EOF and only then called
`StandardError.ReadToEndAsync()`. A child that filled the (~4 KB) stderr pipe buffer before
closing stdout would block writing stderr while we blocked reading stdout — a classic redirect
deadlock. Stderr is now drained concurrently via a task started before the stdout loop.

#### 1.2 Stale vectors never reclaimed on re-index — ✅ Fixed
`VectorSearchService.IndexRepositoryAsync` only upserted files that currently exist, so a deleted
or renamed file left its embedding in `CodeIndex` forever, polluting search and plan context.
The indexer now records the on-disk file set and prunes any indexed file no longer present
(`IVectorStore.GetIndexedFilesAsync` + `DeleteByFileAsync`).

### P2 — RAG quality & cost

#### 2.1 Chunked, symbol-aware embeddings
Today each file becomes a **single** vector, hard-truncated at 8 000 characters
(`VectorSearchService.IndexFileAsync`). Large files lose their tail entirely and a whole-file
embedding dilutes semantic precision. Split files into per-symbol or sliding-window chunks and
embed each separately. `CodeIndexEntry` already carries `SymbolName` / `SymbolKind` (currently
always `"file"`), so the storage schema is ready — this is the natural successor to proposal #2.

#### 2.2 Change-detection to skip unchanged files
There is no content-hash or mtime check, so every re-index re-embeds the entire repository,
burning Gemini embedding quota (or local CPU) on the background auto-index path. Store a content
hash per entry and short-circuit when it matches, embedding only changed files.

### P3 — Maintainability

#### 3.1 Consolidate LLM task-JSON parsing
The "parse a JSON array of `{title, description, verificationCommand, risk, skillTags}` into
`TaskItem`s" logic is duplicated across `GeminiService.ParseTasks`, `SessionView.ParseSubTasks`,
and the decompose flow. Extract one shared parser so the Razor component no longer reaches into
`System.Text.Json` directly and the risk/skill-tag rules live in a single place.

### P4 — Exploration

#### 4.1 Visual pipeline live telemetry
The script graph already renders an active-node highlight and a KPI strip. A future pass could
stream per-node token/duration counters live during a run rather than only at completion.
