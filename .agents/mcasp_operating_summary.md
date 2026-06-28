# MCASP Operating Summary

This document serves as our compact, durable reference for the Most Capable Agent System Prompt (MCASP) architecture and rules within the WheelHouse workspace.

---

## 1. Default Architecture

WheelHouse utilizes a double-agent loop:
*   **Gemini (Planning Brain)**: Indexes the codebase using a local RAG vector DB, maps out implementation plans, parses plans into structured task lists, and diagnoses failures.
*   **Claude Code (Execution)**: A downstream agent running locally inside git-isolated worktrees/branches to execute task descriptions.
*   **PowerShell Runner (Verification)**: Automatically executes tests/builds to verify task completion.
*   **Filesystem-First State (GitOps)**: Active runs are written in real-time to the repo's `.wheelhouse/` directory:
    *   `plan.md`: The active plan context.
    *   `tasks.md`: Structured checklist with checkboxes (`[x]` completed, `[/]` running, `[ ]` pending, `[!]` failed).
    *   `status.md`: Run metadata and active task tracking.
*   **Compounding Memory Loop**: Session completion triggers Gemini to synthesize lessons learned, merging them into `.wheelhouse/knowledge.md` to be injected back into subsequent planning RAG windows.

---

## 2. Completed Milestones

All five phases of the MCASP integration are complete:
1.  **State Sync**: Database models synced in real-time to the repository filesystem.
2.  **Rich Task Schema**: Integration of task risk levels (`Low`/`Medium`/`High`) and technology skill tags.
3.  **Compounding Lessons Learned Loop**: Memory ratchet reading and updating `knowledge.md` on completion.
4.  **Manual Approval Gates**: Pausing execution loop for developer sign-off on High-Risk tasks.
5.  **Dynamic Task Decomposition**: "Decompose & Resolve" action to split verification failures into sub-tasks.

---

## 3. Key Guardrails

*   **Risk Mitigation**: High-risk tasks are flagged, highlighted, and gated. Verification success on High-risk tasks triggers `WorkItemStatus.AwaitingApproval` and blocks execution until explicit user sign-off.
*   **Validation-First**: Tasks are not marked complete until verification tests exit code `0`.
*   **Git Checkpoint Isolation**: Runs are executed on session branches/worktrees to allow instant rollback (`git restore`) on failure.

---

## 4. Runtime Constraints

*   **OS/Shell**: Windows host environment using PowerShell Core for verification execution.
*   **Integration**: Local REST API call to Gemini (API key required) and local terminal execution for Claude Code CLI.
