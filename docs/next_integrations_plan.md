# WheelHouse — Next Integrations & Development Plan

*Drafted 2026-07-01. Companion to [architectural_suggestions.md](architectural_suggestions.md), which tracks
the shipped milestones and the fine-grained backlog. This document is the forward-looking roadmap: what to
integrate next, why it improves performance, and what unfinished work it depends on.*

---

## 1. Where the project stands

WheelHouse v0.1.0 delivers its core objective: a two-model desktop cockpit where Gemini plans,
Claude Code executes, and every task is gated by a verification command. Shipped and working:

- Plan → task checklist → execute → verify loop with per-workspace permission modes.
- Offline RAG (ONNX all-MiniLM-L6-v2 + sqlite-vec, Gemini-embedding fallback) feeding plan context.
- Headroom token compression (`headroom wrap claude`) with auto-detection and fallback.
- Visual scripting graph (`ScriptGraphView` / `ScriptExecutor`) with run metrics.
- Persistent transcripts with full-text search, Markdown export, and session archives.
- GitOps syncer (`.wheelhouse/config.yaml`) and git branch isolation per session.

The remaining gaps cluster into three themes: **RAG precision and indexing cost**, **deeper agent
integration** (Claude sees WheelHouse's knowledge only via prompt injection today), and
**operational polish** (cross-platform verification, packaging, live telemetry).

---

## 2. Leftover development (carry-over backlog)

These are the still-open items from `architectural_suggestions.md`, restated with their concrete
code anchors. They are prerequisites for several integrations below, so they lead the plan.

| # | Item | Anchor | Status |
|---|------|--------|--------|
| L1 | **Chunked, symbol-aware embeddings** — each file is a single vector, hard-truncated at 8 000 chars (`VectorSearchService.IndexFileAsync`); `SymbolKind` is always `"file"` even though `CodeIndexEntry` already carries symbol columns. | `VectorSearchService.cs:49` | Open (P2 §2.1) |
| L2 | **Change-detection on re-index** — no content hash or mtime check, so every re-index re-embeds the whole repository (background auto-index path included). | `VectorSearchService.IndexRepositoryAsync` | Open (P2 §2.2) |
| L3 | **Consolidate LLM task-JSON parsing** — duplicated across `GeminiService.ParseTasks`, `SessionView.ParseSubTasks`, and the decompose flow. | `GeminiService` / `SessionView.razor` | Open (P3 §3.1) |
| L4 | **Visual pipeline live telemetry** — script graph shows active-node highlight and end-of-run KPIs, but no live per-node token/duration counters. | `ScriptGraphView.razor` / `ScriptRunMetrics` | Open (P4 §4.1) |

---

## 3. Next integrations

Ordered by impact-per-effort. Each entry states the objective, the integration work, and what it
depends on.

### Tier 1 — Performance of the RAG pipeline (highest leverage)

#### 3.1 Incremental indexing with content hashes *(completes L2)* — ✅ Shipped
**Objective:** stop re-embedding unchanged files; make background auto-index effectively free.
- `ContentHash` (SHA-256 of the compressed snippet) added to `CodeIndexEntry`
  (`AddCodeIndexContentHash` migration); `VectorSearchService` loads stored hashes once per run via
  `IVectorStore.GetFileHashesAsync` and skips `EmbedAsync` on a match. Covered by
  `IncrementalIndexingTests`.
- Effect: typical re-index touches <5 % of files → 20×+ reduction in embedding cost
  (Gemini quota or local CPU), and the `WorkspaceIndexingService` hosted queue drains in seconds.

#### 3.2 Symbol-aware chunked embeddings *(completes L1)* — ✅ Shipped (sliding-window)
**Objective:** raise retrieval precision and stop losing the tails of large files.
- Shipped: `CodeChunker` (Core) splits compressed source into overlapping line-aligned windows
  (~1 500 chars, ~200 overlap); the 8 000-char truncation is gone; chunks are stored atomically
  per file via the new `IVectorStore.ReplaceFileAsync` and still share one per-file content hash,
  so 3.1's skip logic is untouched. Covered by `CodeChunkerTests` + `ChunkedIndexingTests`.
- Still open (follow-ups):
  - Per-symbol chunk boundaries for C# via Roslyn instead of character windows.
  - Batch chunk embeddings: `LocalOnnxEmbeddingProvider` batched inference and the Gemini
    batch-embed endpoint — both cut indexing wall-time on large repos.
- Note: multiple vectors per file multiplies row count ~5–20×, which makes the sqlite-vec ANN
  path important; the `CosineVectorStore` fallback remains but scans O(n).

#### 3.3 Hybrid retrieval: keyword + vector fusion — ✅ Shipped
**Objective:** better recall for exact identifiers (function names, error strings) that pure
semantic search misses.
- Shipped: `VectorSearchService.SearchAsync` now runs both legs and rank-fuses them
  (`SearchFusion.ReciprocalRankFusion` in Core). The lexical leg
  (`IVectorStore.KeywordSearchAsync` → `CodeIndexKeywordSearch`) uses tokenized `LIKE`
  substring matching rather than the originally proposed FTS5 table — deliberately: it mirrors
  the existing `TranscriptSearchService`, needs no schema/trigger maintenance, and substring
  semantics find partial identifiers (e.g. "FileHashes" inside `GetFileHashesAsync`) that a
  word tokenizer cannot. Search now also degrades gracefully to keyword-only when no embedding
  provider is available. Covered by `SearchFusionTests` + `HybridSearchTests`.
- Zero new external dependencies; works fully offline; directly improves the plan context that
  `SessionView.BuildPlanningContext()` injects.

### Tier 2 — Deeper agent integration

#### 3.4 Expose WheelHouse RAG to Claude via MCP — ✅ Shipped
**Objective:** today Claude only receives RAG context that Gemini's plan happened to include.
Let Claude *query* the index mid-task instead.
- Shipped as an **HTTP MCP endpoint inside the running app** (not the originally sketched stdio
  sidecar — no second process, no duplicate embedding stack, and tools always see the live index):
  - `McpJsonRpcHandler` (Core) — minimal dependency-free JSON-RPC/MCP protocol core, unit-tested.
  - `WheelHouseMcpServer` (Infrastructure) — tools `search_code` (hybrid index search) and
    `get_knowledge` (`.wheelhouse/knowledge.md`), resolving scoped services per call.
  - `POST /mcp` mapped in `WheelHouseWebApp` (both desktop and browser hosts); on startup the
    bound URL is written to `%LOCALAPPDATA%\WheelHouse\mcp-config.json` (`McpEndpointState`).
  - `ClaudeCommand.BuildAgentArgs` appends `--mcp-config <file>` and pre-allows
    `mcp__wheelhouse__search_code` / `mcp__wheelhouse__get_knowledge` so print-mode runs can call
    them without prompting. `WHEELHOUSE_MCP=off` disables the whole feature.
- Verified live: initialize handshake, tools/list, and a tools/call answered from the real index.
- Follow-up: the endpoint is unauthenticated on localhost (fine for a single-user desktop app);
  add a bearer token to the generated config if WheelHouse ever binds beyond loopback.

#### 3.5 Structured output for Gemini task decomposition *(completes L3)*
**Objective:** eliminate the fragile "parse a JSON array out of prose" step entirely.
- Use Gemini's `responseSchema` / JSON mode for the decompose call so the API guarantees the
  `{title, description, verificationCommand, risk, skillTags}` shape, and extract the one shared
  parser (`TaskItemParser` in Core) that L3 calls for. The Razor component stops touching
  `System.Text.Json`.
- Fewer malformed-plan retries = fewer paid planning calls and less user friction.
- **Effort:** small. **Depends on:** nothing; fold L3 into it.

#### 3.6 Gemini context caching for planning — ✅ Shipped
**Objective:** cut planning-token cost on iterative sessions.
- Shipped: `GenerateResearchPlanAsync` and `TroubleshootAsync` now split their prompts into the
  stable repository context and the volatile part. Large contexts
  (≥ `ContextCacheMinChars`, default 16k chars ≈ the API's ~4k-token cache minimum) are uploaded
  once as a Gemini `cachedContents` resource (TTL 600 s, carrying the system preamble) and later
  calls reference it via `cachedContent`, paying only for the delta. `GeminiContextCache` (pure,
  unit-tested) tracks resource names by content hash with early client-side expiry.
- Degrades gracefully at every step: small contexts stay inline, cache-creation failures fall
  back to inline, and a server-rejected cache is invalidated and the call retried inline.
  `WHEELHOUSE_GEMINI_CACHE=off` disables it. Covered by `GeminiContextCachingTests` (scripted
  HTTP handler proves create-once/reference-after, inline fallback, and the off switch).

### Tier 3 — Orchestration & operational polish

#### 3.7 Git worktree isolation for parallel task execution — ✅ Shipped
**Objective:** run independent tasks from one plan concurrently instead of serially.
- Shipped: `IGitService` gained worktree operations (`AddWorktreeAsync`, `RemoveWorktreeAsync`,
  `MergeBranchAsync` — which aborts cleanly on conflict — `DeleteBranchAsync`, `CommitAllAsync`).
  The `parallel` node now has an `Isolation` setting (`shared` default / `worktree`): with
  `worktree`, each branch runs its nodes — Claude edits, verification commands, git commands —
  inside its own linked worktree on a `wheelhouse/parallel-*` branch, so concurrent agents can't
  collide on files. When the branches rejoin, uncommitted agent edits are snapshot-committed,
  each branch is merged back (`--no-ff`), and worktrees/branches are cleaned up. A conflicted
  merge is aborted, the main tree left untouched, and the branch kept with its name in the log
  for manual resolution.
- Covered by worktree round-trip + conflict-abort tests in `GitServiceTests` and an end-to-end
  `ScriptExecutorTests` case proving two parallel agents' files both land in the main repository
  with two merge commits and a clean tree.
- Note: branches base on HEAD — uncommitted changes in the main tree are not visible inside
  isolated branches. Follow-up: conflict-resolution UX in `SessionView` beyond the log message.

#### 3.8 Portable verification commands
**Objective:** *(corrected 2026-07-01: the runner is already cross-platform — despite its name,
`PowerShellVerificationRunner.BuildStartInfo` falls back to `/bin/bash -c` on non-Windows.)*
The remaining gap is command portability: a workspace's verification commands are authored in one
shell dialect, so a `.wheelhouse/config.yaml` written on Windows (PowerShell syntax) breaks for a
teammate on macOS/Linux and vice versa.
- Let a workspace declare its verification shell (`pwsh`/`bash`/`auto`) in the GitOps config, and
  prefer cross-platform invocations (`dotnet test`, `npm test`) in docs/templates.
- **Effort:** small. **Depends on:** nothing. Only matters once non-Windows users share configs.

#### 3.9 Live per-node telemetry *(completes L4)* + cost — ✅ Shipped
**Objective:** make the script graph a real-time cockpit and surface $ cost, not just tokens.
- Shipped: `ClaudeCliService` parses the CLI's final `result` accounting into
  `AgentStreamEvent.Usage` (real input/output tokens incl. cache, `total_cost_usd`,
  `duration_ms`); `ScriptExecutor` prefers those over the chars÷4 estimate, accumulates run cost
  into `ScriptRunMetrics.CostUsd`, and streams a `ScriptNodeTelemetry` per finished node over a
  new optional callback. `ScriptGraphView` renders the counters as a live badge on each node
  (duration · tokens · $), red on engine failure, and the KPI strip shows total cost.
  Verified in-browser; covered by `AgentUsageParsingTests` + new `ScriptExecutorTests` cases.
- Note: per-node token/cost deltas are approximate under parallel branches (shared accumulator).
- Follow-up: surface Headroom's compression savings alongside (via its stats endpoint).

#### 3.10 File-watcher incremental auto-index — ✅ Shipped
**Objective:** replace manual/queue-driven full re-index with event-driven freshness.
- Shipped: `WorkspaceWatchService` keeps a `RepositoryWatcher` (`FileSystemWatcher`, debounced
  ~2 s quiet period, filtered through the shared `IndexableFiles` rules) on every workspace root
  and enqueues the workspace on the existing `IWorkspaceIndexQueue` when source changes settle.
  Because of 3.1's hash skip, the resulting re-index only embeds what actually changed — one
  design simplification vs. the original proposal, which called for per-file jobs; whole-workspace
  enqueue reuses the queue unchanged and the hash check makes it equivalent in embedding cost.
  Watchers reconcile against the workspace table every 30 s (workspace add/remove/move needs no
  restart); `WHEELHOUSE_WATCH=off` disables it. Covered by `IndexableFilesTests` +
  `RepositoryWatcherTests`.
- Follow-up (cheap): per-file jobs would also skip the read+hash pass over unchanged files on
  very large repositories.

### Tier 4 — Distribution (when the above lands)

- **Packaging & auto-update:** single-file publish of `WheelHouse.Desktop`, an installer
  (MSIX or Velopack), and update checks — v0.1.0 still requires a full SDK build from source.
- **ONNX runtime tuning:** ship/document an int8-quantized MiniLM variant and enable the DirectML
  execution provider on Windows for GPU-accelerated local embeddings (helps 3.2's larger chunk
  volumes).
- **First-run model download:** offer to fetch `model.onnx`/`vocab.txt` from the Status page
  instead of the manual `curl` steps in the README.

---

## 4. Suggested sequence

```
Sprint A (perf foundations):   3.1 hashes ✅ → 3.5 structured output (+L3) → 3.8 portable verify commands
Sprint B (retrieval quality):  3.2 chunking (+L1) ✅ → 3.3 hybrid search ✅ → 3.10 file watcher ✅
Sprint C (agent depth):        3.4 MCP server ✅ → 3.6 context caching ✅ → 3.9 live telemetry (+L4) ✅
Sprint D (scale & ship):       3.7 worktree parallelism ✅ → Tier 4 packaging
```

## 5. Success metrics

| Metric | Today (baseline) | Target |
|--------|------------------|--------|
| Re-index time, unchanged repo | full re-embed of every file | < 5 s (hash short-circuit) ✅ |
| Retrieval granularity | 1 vector/file, 8 000-char cap | overlapping window chunks, no truncation loss ✅ |
| Exact-identifier search recall | vector-only | hybrid keyword + vector rank fusion ✅ |
| Claude context acquisition | plan-time prompt injection only | on-demand MCP queries during execution ✅ |
| Decompose parse failures | prose-JSON extraction, 3 duplicated parsers | schema-enforced, 1 shared parser |
| Verification command portability | shell dialect implicit (PowerShell on Windows, bash elsewhere) | per-workspace shell declared in GitOps config |
| Multi-task plan wall-clock | serial execution | parallel via git worktrees ✅ |
| Run observability | end-of-run KPIs | live per-node tokens/duration + cost ✅ |
