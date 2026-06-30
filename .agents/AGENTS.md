# WheelHouse Agent Workspace Guidelines

This directory contains workspace-scoped rules and instructions for coding agents operating within the WheelHouse repository.

## 1. Filesystem-First State & Progress
The active session state of the WheelHouse runner is synchronized in real-time to the `.wheelhouse/` folder at the repository root:
*   `.wheelhouse/plan.md`: The active implementation plan (objective, steps, verification criteria, risks, open questions, next/blocked).
*   `.wheelhouse/tasks.md`: The list of tasks with their verification commands, outputs, risk level, skill tags, and status (`[x]` completed, `[/]` in-progress/verifying, `[ ]` pending, `[!]` failed).
*   `.wheelhouse/status.md`: Overall session status and the currently active task.
*   `.wheelhouse/handoff.md`: Live momentum queues — Now (active), Next (ready), Awaiting approval, Blocked (failed), plus a one-line continuation instruction. Read this first to know exactly where to resume.

**Guidelines for Agents**:
*   Before performing any work, read `handoff.md` then inspect `plan.md`/`tasks.md` to verify the active task you are assigned to.
*   High-risk tasks gate on human approval after verification passes — do not bypass the approval state.
*   Do not modify these state files directly unless you are the orchestrator service updating status.

## 2. Compounding Repository Knowledge
*   `.wheelhouse/knowledge.md` is a persistent knowledge base written at the end of successful sessions to record learnings, architectural decisions, database schemas, API structures, and codebase quirks.
*   **Critical**: Always read `.wheelhouse/knowledge.md` before starting research or planning. You must inherit and apply these previous lessons learned to prevent repeating mistakes or violating codebase rules.
