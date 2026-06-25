# Changelog

All notable changes to WheelHouse are documented here. This project adheres loosely to
[Keep a Changelog](https://keepachangelog.com/) and [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Two-model orchestration: Gemini planning + Claude Code execution.
- Test-Driven Handoff: plan → task checklist → execute → verification command gates completion,
  with "Ask Gemini for a fix" on failure.
- Per-workspace Claude permission mode and auto-approve rules (mapped to `--allowedTools` /
  `--disallowedTools`) so task runs can autonomously edit code.
- Optional Headroom token compression (`headroom wrap claude`), auto-detected with fallback.
- Offline-capable RAG: on-device ONNX embeddings + sqlite-vec, falling back to Gemini + cosine.
- Persistent session transcripts; history with filters, rename, reopen, delete, and Markdown export.
- Full-text transcript search with highlighted snippets.
- Prompt template library (6 built-in R&D templates).
- System Status page and in-app Settings (branding + defaults).
- `.env` loader and EF Core migrations.

### Notes
- First public release preparation: MIT license, configurable branding, de-personalized defaults.
