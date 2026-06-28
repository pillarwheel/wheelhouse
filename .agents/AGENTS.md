# WheelHouse Agent Workspace Guidelines

This directory contains workspace-scoped rules and instructions for coding agents operating within the WheelHouse repository.

## 1. Filesystem-First State & Progress
The active session state of the WheelHouse runner is synchronized in real-time to the `.wheelhouse/` folder at the repository root:
*   `.wheelhouse/plan.md`: The active implementation plan.
*   `.wheelhouse/tasks.md`: The list of tasks, verification commands, outputs, and their status (`[x]` for completed, `[/]` for in-progress, `[ ]` for pending, and `[!]` for failed).
*   `.wheelhouse/status.md`: Overall session status.

**Guidelines for Agents**:
*   Before performing any work, inspect these files to verify the active task you are assigned to.
*   Do not modify these state files directly unless you are the orchestrator service updating status.

## 2. Compounding Repository Knowledge
*   `.wheelhouse/knowledge.md` is a persistent knowledge base written at the end of successful sessions to record learnings, architectural decisions, database schemas, API structures, and codebase quirks.
*   **Critical**: Always read `.wheelhouse/knowledge.md` before starting research or planning. You must inherit and apply these previous lessons learned to prevent repeating mistakes or violating codebase rules.
