# WheelHouse Usage Guide & Walkthrough

Welcome to the official usage guide for **WheelHouse**, Pillarwheel Studio's codebase cockpit designed to orchestrate research, planning, and code development using a specialized double-agent loop: **Gemini** (for long-context planning) and **Claude Code** (for agentic code execution).

This document details the system architecture, installation steps, core capabilities, and a step-by-step developer walkthrough.

---

## 1. Core Architecture Overview

WheelHouse is built as a modular .NET 8 application with Blazor Server and a native Photino desktop shell:

```
  ┌────────────────────────────────────────────────────────┐
  │                   WheelHouse Desktop                   │
  │                  (Photino Window Shell)                │
  └───────────────────────────┬────────────────────────────┘
                              │ Host Web
  ┌───────────────────────────▼────────────────────────────┐
  │                     WheelHouse.Web                     │
  │                (Blazor Server Dashboard)               │
  └───────────────────────────┬────────────────────────────┘
                              │ EF Core / DI
  ┌───────────────────────────▼────────────────────────────┐
  │               WheelHouse.Infrastructure                │
  │        (DB, RAG, Claude Service, PowerShell Runner)     │
  └────────────────────────────────────────────────────────┘
```

### The Double-Agent Orchestration Loop
1. **Gemini (Planning)**: Utilizes a semantic RAG index of your repository to draft structured plans.
2. **Claude Code (Execution)**: Takes task-level prompts from the plan and writes build-ready modifications.
3. **PowerShell (Verification)**: Automatically executes test suites (`dotnet test`, `npm test`) to verify Claude's work.
4. **Gemini (Troubleshooting)**: Diagnoses build or test failures by reading the output logs and providing a diff fix.

---

## 2. Setup & Configuration

### Step A: API Keys (.env)
Create a `.env` file in the root of the project (or alongside the built executable) using [.env.example](file:///g:/Code/wheelhouse/.env.example) as a guide:
```ini
GEMINI_API_KEY=your-gemini-key
ANTHROPIC_API_KEY=your-anthropic-key

# Optional Headroom Context Compression: auto / on / off
WHEELHOUSE_HEADROOM=auto
WHEELHOUSE_HEADROOM_PATH=
```

### Step B: Local Embedding Model (Offline RAG)
WheelHouse indexes and searches your codebase locally. Run the following setup script to download the ONNX weights for `all-MiniLM-L6-v2`:
```powershell
# PowerShell Setup
$Dir = "$env:LOCALAPPDATA\WheelHouse\models\all-MiniLM-L6-v2"
New-Item -ItemType Directory -Force -Path $Dir

# Download vocab & model weights
Invoke-WebRequest -Uri "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt" -OutFile "$Dir\vocab.txt"
Invoke-WebRequest -Uri "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx" -OutFile "$Dir\model.onnx"
```

### Step C: Claude CLI & Headroom Installation
1. Install [Claude Code CLI](https://docs.claude.com/claude-code):
   ```bash
   npm install -g @anthropic-ai/claude-code
   ```
2. (Optional) Install [Headroom Context Compressor](https://github.com/headroomlabs-ai/headroom):
   ```bash
   pip install "headroom-ai[all]"
   ```

### Step D: Headroom Auth & 401 Resolution
When Headroom is active, it runs a local proxy setting `ANTHROPIC_BASE_URL`.
* **Important**: If you are logged into a **Claude Code subscription session**, Claude sends OAuth headers instead of `x-api-key`, causing a `401 authentication_failed` error.
* **Resolution**: Run `claude logout` in your terminal to switch Claude Code to API-key-only mode. It will then authenticate through Headroom using the environment's `ANTHROPIC_API_KEY`.

---

## 3. Core Features Guide

### Workspace Management & GitOps Config
* **Registering**: On the main dashboard, add a workspace by providing a display name and local absolute directory path.
* **GitOps Syncer**: Settings are version-controlled next to the code. Click **Sync YAML** to mirror the workspace settings and auto-approve command patterns to `.wheelhouse/config.yaml` inside your repository.

### Local Code Indexing (RAG)
* **Extension Coverage**: RAG supports C#, TypeScript, JavaScript, Python, Razor, Markdown, JSON, and YAML.
* **Comment & AST Compression**: When indexing source code, WheelHouse automatically strips comments, doc-comments, and redundant spaces (`CodeCompressionService`) to reduce embedding size and preserve token capacity.
* **Semantic Search**: Use the `/search` panel to query codebase entities using local vector matching over sqlite-vec or in-memory Cosine stores.

### Git Branch Isolation & Checkpointing
To keep your main branch clean:
1. Click **Branch for this session** in the session header.
2. WheelHouse automatically spawns `wheelhouse/session-{id}`.
3. If Claude makes mistakes, click **Discard changes** next to the troubleshooter to run `git restore .` instantly.

### Filesystem-First State Synchronization (GitOps)
* **Live Synchronization**: During session execution, WheelHouse dynamically updates files in the repository root's `.wheelhouse/` folder:
  *   `.wheelhouse/plan.md`: The active planning context.
  *   `.wheelhouse/tasks.md`: A live markdown task checklist with progress checkboxes (`[x]` for completed, `[/]` for in-progress/verifying, `[ ]` for pending, and `[!]` for failed).
  *   `.wheelhouse/status.md`: Run metadata including session status, repository path, active task, and last updated time.

### Rich Task Schema & Skill Badges
* **Risk Levels**: Gemini automatically assesses risk levels (`Low`, `Medium`, `High`) for generated tasks, shown with color-coded badges in the UI. High-risk tasks are highlighted with warning borders.
* **Skill Tags**: Tasks are annotated with relevant skill tags (e.g. `csharp`, `database`, `ui`) to help developers instantly understand the required expertise.

### Compounding Lessons Learned Loop
* **Durable Knowledge Base**: On successful session completion, WheelHouse compiles learnings, architectural decisions, and gotchas discovered during the run to `.wheelhouse/knowledge.md` in the repository.
* **Auto-Feeding**: Subsequent planning sessions automatically read `.wheelhouse/knowledge.md` and inject its contents into the planning context, allowing future runs to benefit from previous experiences.

### Manual Approval Gates
* **High-Risk Verification Guardrails**: When a task marked as **High Risk** passes automated verification tests, the session execution pauses and transitions to `AwaitingApproval`. The developer is prompted to review the changes and click **Approve** or **Reject** before the runner proceeds.

### Dynamic Task Decomposition
* **Troubleshooting Breakdown**: If a task fails verification, developers can click **Decompose & Resolve**. Gemini will analyze the failure log and insert a sequence of smaller, targeted troubleshooting and recovery sub-tasks directly into the execution list to step-by-step resolve the bug.

---

## 4. Step-by-Step Developer Walkthrough

Let's walk through an orchestration session from setup to completion.

### Phase 1: Indexing a Repository
1. Navigate to the **Code Search** page.
2. Select your repository in the dropdown.
3. Click **Index Selected**. Wait for files to be processed and stored.

### Phase 2: Planning with Gemini
1. Navigate to the Dashboard and click **New Session** for your repository.
2. Describe your goal: *"Implement a new endpoint in UserController.cs to retrieve user profiles by email, and verify it with a unit test."*
3. Click **Generate Plan**.
   * **Under the hood**: WheelHouse semantic-searches your RAG database for terms like `UserController` or `User`, retrieves relevant C# code snippets, injects them into Gemini's context window as `RagContext`, and calls Gemini to write a precise implementation guide.

### Phase 3: Breaking Plan into Tasks
1. Under the generated plan, click **Generate Task List**.
2. Gemini parses the markdown implementation steps and generates discrete `TaskItem` database models, each having:
   * A Sequence.
   * Title & Description.
   * A `VerificationCommand` (e.g. `dotnet test --filter UserControllerTests`).

### Phase 4: Git Branch Checkpoint
1. In the session panel, check the Git status bar.
2. Click **Branch for this session** to isolate Claude's edits.

### Phase 5: Executing & Verifying
1. Select the first task (e.g., *Create email endpoint*).
2. Click **Run Task** in the execution pane.
   * **Under the hood**: The dashboard streams Claude Code CLI stdout in real time as it builds the endpoint.
3. Once Claude finishes, the pipeline enters the **Verify** stage.
   * **Under the hood**: The dashboard triggers `PowerShellVerificationRunner` to run the task's verification command (`dotnet test`).
4. If the test passes, the task turns **Green** (Completed).
5. If the test fails:
   * Click **Ask Gemini for a fix**.
   * Gemini analyzes the test console failure and prints a diagnostics report with the corrected code.
   * Click **Discard Changes** to reset the working tree, modify your prompt, and run again.

### Phase 6: Saving the Transcript
1. Once all tasks are complete, click **Save to workspace** in the session export footer.
2. A clean markdown record is saved under `.wheelhouse/sessions/<session-id>.md` in your repository.
3. Merge your isolation branch into `main`.
