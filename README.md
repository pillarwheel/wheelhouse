<div align="center">

# WheelHouse

**A desktop cockpit for AI-assisted coding: research, plan, execute, verify.**

Gemini plans and researches. Claude Code executes. Everything is observed, verified, and
recorded from one glassy dashboard — with optional token compression and fully-offline RAG.

[![CI](https://github.com/pillarwheel/wheelhouse/actions/workflows/ci.yml/badge.svg)](https://github.com/pillarwheel/wheelhouse/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-22e3d6.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)

</div>

---

## Documentation

For step-by-step walkthroughs, navigation breadcrumbs, and deep-dive technical configurations, check out:
* **[Comprehensive User & Developer Guide](docs/comprehensive_guide.md)** — Core walkthroughs for the Visual Scripting graph editor, Headroom troubleshooting, and GitOps syncer configuration.
* **[Usage Guide & Walkthrough](docs/usage.md)** — Step-by-step guide to repository indexing, Gemini planning, task decomposition, and local execution.
* **[Architectural Suggestions](docs/architectural_suggestions.md)** — Shipped milestones plus the active backlog for local RAG optimization, multi-language code compression, and branch checkpoints.
* **[Next Integrations & Development Plan](docs/next_integrations_plan.md)** — Forward-looking roadmap: RAG performance work, deeper Claude/Gemini integration, and the remaining backlog sequenced into sprints.

---

## What is it?

WheelHouse orchestrates a two-model coding workflow on your own machine:

1. **Research & plan** — Gemini turns a goal into a markdown implementation plan.
2. **Break it down** — the plan becomes a checklist of verifiable tasks (Test-Driven Handoff).
3. **Execute** — Claude Code runs each task and *actually edits your code* (configurable permissions).
4. **Verify** — each task's verification command (e.g. `dotnet test`) must pass for it to complete.
5. **Review** — every run is streamed to a console and saved as a persistent, searchable transcript.

It runs as a native desktop app (Photino) or in your browser (Blazor Server), backed by local SQLite.

## Features

- **Two-model orchestration** — Gemini for planning/research, Claude Code for execution.
- **Cost-cascade execution (default)** — verifiable tasks try the cheap tier (Gemini, isolated in a
  git worktree and grounded by code search) first; only failures escalate to Claude Code.
- **Tool governance** — per-workspace MCP policy (`.wheelhouse/mcp-policy.json`) enforced at the
  Claude launcher and the MCP server (timeouts, call budgets, audit log), plus a static security
  audit of auto-approve command rules.
- **Darwin mode & benchmark scorecard** — an evolutionary loop mutates the harness genome (prompts,
  retrieval depth/balance) and can score generations against a real plan→execute→verify benchmark
  suite; fine-tuning exports (SFT/DPO JSONL) turn transcripts into training data.
- **Test-Driven Handoff** — plan → task checklist → Claude executes → verification command gates completion → one-click "Ask Gemini for a fix" on failure.
- **Autonomous execution & permissions** — per-workspace Claude permission mode (`acceptEdits` by default) and auto-approve rules that map to Claude `--allowedTools` / `--disallowedTools`.
- **Token compression (optional)** — routes Claude through [Headroom](https://github.com/headroomlabs-ai/headroom) (`headroom wrap claude`) to cut context tokens; auto-detected with graceful fallback.
- **Offline-capable RAG** — hybrid (semantic + keyword) code search via on-device ONNX embeddings (all-MiniLM-L6-v2) + [sqlite-vec](https://github.com/asg017/sqlite-vec) ANN, falling back to Gemini embeddings + cosine. Chunked incremental indexing keeps itself fresh via a file watcher.
- **MCP tools for Claude** — every Claude run gets `search_code` / `get_knowledge` MCP tools served by the app itself, so the agent can query your indexed code mid-task.
- **Persistent, searchable transcripts** — full session history with filters, rename, reopen, delete, Markdown export, and full-text search across all transcripts.
- **Prompt template library** — 6 built-in parameterized R&D templates, plus your own.
- **System Status & in-app settings** — live health of every integration; configurable company/branding.
- **Premium dark UI** — glassmorphism, deep slate / neon cyan / electric purple.

## Quick start

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (pinned via `global.json`)
- [Claude Code CLI](https://docs.claude.com/claude-code) on your `PATH` (`claude`), signed in (`claude login`)
- A Gemini API key (optional — only for planning and cloud embeddings)

### Build & run
```bash
git clone <your-fork-url> wheelhouse
cd wheelhouse
cp .env.example .env          # then fill in keys (see Configuration)

dotnet build
dotnet test                   # 199 offline tests

# Desktop app (native window):
dotnet run --project src/WheelHouse.Desktop
# …or in a browser:
dotnet run --project src/WheelHouse.Web
```

The SQLite database is created/upgraded on first run (EF migrations) under
`%LOCALAPPDATA%\WheelHouse\wheelhouse.db` (Windows).

## Configuration

### `.env` (secrets & infrastructure)
Copy [`.env.example`](.env.example) to `.env`. Real OS environment variables override the file, and
`.env` is git-ignored. Keys:

| Variable | Purpose |
|---|---|
| `GEMINI_API_KEY` | Gemini planning, troubleshooting, cloud-embedding fallback (optional) |
| `ANTHROPIC_API_KEY` | Direct (non-Headroom) Claude use (optional) |
| `WHEELHOUSE_HEADROOM` | `auto` (default) / `on` / `off` |
| `WHEELHOUSE_HEADROOM_PATH` | Explicit path to the `headroom` executable |
| `WHEELHOUSE_WATCH` | `auto` (default) — re-index workspaces automatically when source files change; `off` disables |
| `WHEELHOUSE_MCP` | `auto` (default) — expose the code index to Claude as MCP tools (`search_code`, `get_knowledge`); `off` disables |
| `WHEELHOUSE_GEMINI_CACHE` | `auto` (default) — cache large repository contexts server-side (Gemini explicit caching) across plan/fix calls; `off` disables |
| `WHEELHOUSE_CASCADE` | `auto` (default) — try the cheap Gemini tier before Claude on verifiable tasks; `off` always routes straight to Claude Code |

### In-app Settings
Branding (company name, product name, tagline) and the default workspace permission mode are
edited in-app on the **Settings** page and stored in the database.

### Local embedding model (offline RAG)
Place `model.onnx` + `vocab.txt` for `all-MiniLM-L6-v2` in
`%LOCALAPPDATA%\WheelHouse\models\all-MiniLM-L6-v2\`:
```bash
DIR="$LOCALAPPDATA/WheelHouse/models/all-MiniLM-L6-v2"; mkdir -p "$DIR"
BASE="https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main"
curl -L -o "$DIR/vocab.txt"  "$BASE/vocab.txt"
curl -L -o "$DIR/model.onnx" "$BASE/onnx/model.onnx"
```
With it present, embeddings (and therefore code search) run fully offline.

### Token compression (Headroom)
Install Headroom and WheelHouse routes Claude through it automatically:
```bash
pip install "headroom-ai[all]"   # or: npm install -g headroom-ai
```
**Authentication note:** Headroom routes Claude through a local proxy (`ANTHROPIC_BASE_URL`). Claude
Code sends credentials to a custom base URL as a Bearer token — Anthropic accepts that for a
**subscription/OAuth login** (`claude login`) but **rejects a raw API key** in Bearer form (it needs
`x-api-key`). So use Headroom with a Claude subscription login; an `ANTHROPIC_API_KEY` still works for
direct, non-Headroom use.

## Architecture

A .NET 8 solution, five projects:

| Project | Responsibility |
|---|---|
| `WheelHouse.Core` | Domain models, interfaces, pure logic (rendering, permissions, search snippets) |
| `WheelHouse.Infrastructure` | EF Core + SQLite, Claude CLI runner, Gemini service, RAG, verification runner |
| `WheelHouse.Web` | Blazor Server dashboard + design system (runs standalone in a browser) |
| `WheelHouse.Desktop` | Photino native shell hosting the web app |
| `WheelHouse.Tests` | xUnit tests |

Schema changes go through EF migrations (applied on startup), so updates never drop your data.

## Testing
```bash
dotnet test                                   # 199 fast, offline tests
WHEELHOUSE_LIVE_TESTS=1 dotnet test           # also runs gated live API tests (needs keys/login)
```

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). In short: keep it building,
keep the offline tests green, and match the surrounding code style.

## Support

If WheelHouse is useful to you, you can support development here:
**[☕ ko-fi.com/pillarwheel](https://ko-fi.com/pillarwheel)**

## License

[MIT](LICENSE) © Pillarwheel Studio.

WheelHouse builds on excellent open-source and third-party tools — Claude Code, the Gemini API,
Headroom, sqlite-vec, ONNX Runtime, and Photino — each under their own licenses.
