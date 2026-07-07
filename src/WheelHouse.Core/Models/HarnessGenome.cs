namespace WheelHouse.Core.Models;

/// <summary>
/// The harness configuration genome, containing prompt templates and RAG retrieval parameters
/// mutated and selected by Darwin Mode for self-improvement. The defaults are the canonical
/// texts/values the harness uses when no genome file exists — so an auto-created genome changes
/// nothing until evolution actually mutates it.
/// </summary>
public class HarnessGenome
{
    /// <summary>The canonical Gemini system preamble (single source of truth; GeminiService falls back to this).</summary>
    public const string DefaultPreamble =
        "You are the principal architect and planning brain of WheelHouse, a maximally capable, self-improving agentic operating system for computer-based work.\n" +
        "Your long-term objective is to coordinate, perform, verify, and improve work across coding, operations, research, planning, and multi-step project execution.\n" +
        "Always prioritize a working system, observable architectures, and a transparent file-first state model over beautiful descriptions or complex abstractions.\n" +
        "Produce precise, build-ready engineering guidance for downstream coding agents (Claude Code).\n" +
        "Focus on closing the loop: goal -> task graph -> execution -> verification -> memory update -> learning.\n" +
        "Be concrete, reference file paths, and prefer verifiable steps.";

    /// <summary>The canonical planning instruction block appended after the goal.</summary>
    public const string DefaultPlanningTemplate =
        "Produce a concise, build-ready markdown implementation plan for a downstream coding agent. " +
        "Close the loop: every step must be executable and verifiable. Use exactly these sections:\n" +
        "1. **Objective** — one sentence on the outcome.\n" +
        "2. **Context Summary** — only what is needed from the repository context above; reference concrete file paths.\n" +
        "3. **Implementation Steps** — numbered, ordered, each naming the files to touch and the change to make.\n" +
        "4. **Verification Criteria** — for each step or for the plan, the exact build/test command that proves success " +
        "(prefer `dotnet build` / `dotnet test`); no guessed file-existence checks.\n" +
        "5. **Risks & Mitigations** — what could break and how to contain it; flag anything High risk.\n" +
        "6. **Open Questions** — anything ambiguous that should be confirmed before risky changes.\n" +
        "7. **Next / Blocked** — the immediate next action after this plan, and anything blocking progress.\n\n" +
        "Prefer transparent, file-first changes and reversible steps. Be concrete over comprehensive.";

    public string GeminiSystemPreamble { get; set; } = DefaultPreamble;

    public int RagTopN { get; set; } = 5;

    public double KeywordWeight { get; set; } = 0.5;

    public string PlanningPromptTemplate { get; set; } = DefaultPlanningTemplate;
}
