# WheelHouse Suggestions and Architectural Improvements

This document outlines key suggestions and architectural improvements to advance WheelHouse's core orchestration design and implementation.

---

## 1. RAG-Powered Research Planning
### The Issue
Currently, when a user sets a goal and clicks **Generate Plan**, `SessionView.razor` passes only the static repository path to Gemini:
```csharp
var plan = await Gemini.GenerateResearchPlanAsync(_goal, $"Repository: {_session.RepositoryPath}");
```
Gemini is forced to guess the structure of the repository, leading to generic plans that may not target the correct files.

### The Improvement
Inject semantic search context directly into the plan generation!
1. Inject the `IVectorSearchService` into `SessionView.razor`.
2. Before calling `GenerateResearchPlanAsync`, run a semantic search using the user's `_goal` as the query:
   ```csharp
   var searchResults = await SearchService.SearchAsync(_goal, topN: 5, repositoryPath: _session.RepositoryPath);
   var codeSnippetContext = string.Join("\n\n", searchResults.Select(r => 
       $"File: {Path.GetRelativePath(_session.RepositoryPath, r.Entry.FilePath)}\n```\n{r.Entry.Snippet}\n```"));
   ```
3. Pass `codeSnippetContext` as the repository context parameter to Gemini. 
4. **Result**: Gemini will produce plans referencing actual classes, symbols, and files in the repository.

---

## 2. Multi-Language AST-Lite Compression
### The Issue
Currently, the `CodeCompressionService.cs` only compresses C# (`.cs`) files:
```csharp
var snippet = filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
    ? _compression.Compress(raw)
    : raw;
```
For TypeScript, JavaScript, Python, or Razor, the raw code is indexed without comments or whitespace stripped, reducing embedding efficiency and consuming more tokens.

### The Improvement
Expand `CodeCompressionService` to handle other C-style syntax languages (`.js`, `.ts`, `.tsx`, `.razor`, `.cpp`).
- Standardize the single-line comment strip logic (which handles `//`) and block comment strip (`/* ... */`) to apply to all C-style extensions.
- This will increase local RAG capacity by 30-50% for modern full-stack web projects.

---

## 3. Session Transcript Export (Markdown Archive)
### The Issue
The planning context and Claude execution events are stored in SQLite but aren't easily shareable outside the app or with other team members.

### The Improvement
Implement a **Download Transcript** or **Save to Workspace** button.
- Compile the session state into a single markdown file (`SESSION_SUMMARY.md`) and save it to `.wheelhouse/sessions/<session-id>.md`.
- File structure:
  ```markdown
  # Session: [Name]
  - Repository: `[Path]`
  - Started: [Timestamp]
  
  ## Goal
  [Goal text]
  
  ## Gemini Plan
  [PlanningContext Markdown]
  
  ## Task Execution List
  1. **[Task 1 Title]** - Completed ✅
     - *Verification*: `[dotnet test]`
     - *Output*: `[Verification Logs]`
  ```
- This fits the **GitOps** approach of WheelHouse, allowing developers to check planning and execution history into git.

---

## 4. Git Branch Isolation and Safe Rollbacks
### The Issue
Claude Code directly edits the working directory. If a task goes wrong or makes unintended modifications, the developer has to manually run `git restore` or clean their working tree.

### The Improvement
Integrate automated branch management into the session setup:
* When starting a new session, WheelHouse can check if the repo is clean and suggest creating a workspace-specific git branch (e.g. `wheelhouse/session-{session-id}`).
* If a verification command fails, add a **Discard Changes** button next to the troubleshooting action that performs a clean git checkout for files changed in that task.

---

## 5. Visual Pipeline UI Canvas
A visual representation of the double-agent workflow loop increases user trust and clarity. We can add a flow visualization panel using a lightweight canvas or CSS diagram in `SessionView.razor` showing the current active stage.
