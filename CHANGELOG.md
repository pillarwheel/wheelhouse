# Changelog

All notable changes to WheelHouse are documented here. This project adheres loosely to
[Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-06-29

First public release.

### Added
- Two-model orchestration: Gemini planning + Claude Code execution.
- Test-Driven Handoff: plan → task checklist → execute → verification command gates completion,
  with "Ask Gemini for a fix" and "Decompose & Resolve" on failure.
- Risk-aware tasks: per-task risk level and skill tags, with a human-approval gate for High-risk
  tasks after verification passes.
- Visual scripting: node-graph workflow editor (`ScriptGraphView`) with run metrics, plus a linear
  pipeline fallback.
- Filesystem-first state sync to `.wheelhouse/` (`plan.md`, `tasks.md`, `status.md`, `handoff.md`)
  so any compatible agent can resume from the folder alone.
- Compounding repository knowledge: a lessons-learned loop writes `.wheelhouse/knowledge.md`, fed
  back into future planning.
- Per-workspace Claude permission mode and auto-approve rules (mapped to `--allowedTools` /
  `--disallowedTools`) so task runs can autonomously edit code.
- Optional Headroom token compression (`headroom wrap claude`), auto-detected with fallback.
- Offline-capable RAG: on-device ONNX embeddings + sqlite-vec, falling back to Gemini + cosine.
  Multi-language AST-lite compression (~20 C-style languages plus hash-comment languages).
- Persistent session transcripts; history with filters, rename, reopen, delete, and Markdown export.
- Full-text transcript search with highlighted snippets.
- Prompt template library (6 built-in R&D templates).
- System Status page and in-app Settings (branding + defaults).
- `.env` loader and EF Core migrations.

### Fixed
- Concurrent stderr drain in the Claude CLI runner to prevent a pipe-buffer redirect deadlock.
- Re-indexing now prunes embeddings for deleted/renamed files so stale vectors no longer pollute
  search and plan context.

### Notes
- MIT license, configurable branding, de-personalized defaults.
