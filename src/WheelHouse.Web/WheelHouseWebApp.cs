using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Core.Models.Script;
using WheelHouse.Infrastructure;
using WheelHouse.Infrastructure.Configuration;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Web;

/// <summary>
/// Builds the shared ASP.NET Core + Blazor Server host. Reused by both the standalone
/// web entry point and the Photino desktop shell so configuration lives in one place.
/// </summary>
public static class WheelHouseWebApp
{
    public static WebApplication Build(string[] args, string? urls = null)
    {
        // Load .env before anything reads environment variables (options do so at registration).
        var envCount = EnvFile.Load();

        var builder = WebApplication.CreateBuilder(args);
        if (urls is not null) builder.WebHost.UseUrls(urls);

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddWheelHouseInfrastructure();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
            app.UseExceptionHandler("/Error", createScopeForErrors: true);

        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapRazorComponents<Components.App>().AddInteractiveServerRenderMode();

        if (envCount > 0)
            app.Logger.LogInformation("Loaded {Count} variable(s) from .env", envCount);

        EnsureDatabase(app);
        return app;
    }

    private static void EnsureDatabase(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WheelHouseDbContext>();
        db.Database.Migrate();

        // Seed the built-in prompt-template library (idempotent).
        var templates = scope.ServiceProvider.GetRequiredService<IPromptTemplateService>();
        templates.SeedBuiltInsAsync().GetAwaiter().GetResult();

        SeedFlowTemplates(db);
    }

    /// <summary>Seeds the default flow plus the example visual-script templates (idempotent by name).</summary>
    private static void SeedFlowTemplates(WheelHouseDbContext db)
    {
        SeedTemplate(db, "Default Agent Flow",
            "Gemini plans, Claude Code executes. The standard WheelHouse pipeline.", graph: null);

        SeedTemplate(db, "Planner-Executor (Gemini → Claude)",
            "Gemini drafts a plan, Claude implements it, then dotnet build verifies. (research_notes pattern A)",
            graph: BuildPlannerExecutor());

        SeedTemplate(db, "Evaluator-Optimizer (Critic-Refiner Loop)",
            "Claude implements, build runs; on failure Gemini diagnoses and the loop retries. (pattern C)",
            graph: BuildEvaluatorOptimizer());

        SeedTemplate(db, "AI Gossiping (Claude ↔ Gemini Dialogue)",
            "Claude and Gemini debate the approach for a few rounds, then tasks are generated. (pattern D)",
            graph: BuildDialogue());

        SeedTemplate(db, "Parallel Pipeline (Backend + Frontend → Merge)",
            "Claude builds the backend while Gemini drafts the frontend concurrently, then Claude reviews the merge. (pattern B)",
            graph: BuildParallelPipeline());

        db.SaveChanges();
    }

    private static void SeedTemplate(WheelHouseDbContext db, string name, string description, ScriptGraph? graph)
    {
        if (db.SessionTemplates.Any(t => t.Name == name)) return;

        db.SessionTemplates.Add(new SessionTemplate
        {
            Name = name,
            Description = description,
            Steps =
            [
                new FlowStepConfiguration { StepType = FlowStepType.Planning, ServiceName = "Gemini" },
                new FlowStepConfiguration { StepType = FlowStepType.Task,     ServiceName = "Gemini" },
                new FlowStepConfiguration { StepType = FlowStepType.Execute,  ServiceName = "ClaudeCode" },
                new FlowStepConfiguration { StepType = FlowStepType.Verify,   ServiceName = "ClaudeCode" },
            ],
            GraphJson = graph?.ToJson()
        });
    }

    private static ScriptNode Node(string id, string type, string name, double x, double y,
        Dictionary<string, string>? settings = null) =>
        new() { Id = id, Type = type, Name = name, X = x, Y = y, Settings = settings ?? new() };

    private static ScriptEdge Edge(string src, string srcPort, string tgt, string tgtPort) =>
        new() { SourceNodeId = src, SourcePort = srcPort, TargetNodeId = tgt, TargetPort = tgtPort };

    private static ScriptGraph BuildPlannerExecutor() => new()
    {
        Nodes =
        {
            Node("start", "start", "Start", 60, 140),
            Node("plan", "gemini-prompt", "Plan", 320, 140,
                new() { ["Prompt"] = "Produce a concise, step-by-step implementation plan for this goal:\n\n{{GOAL}}" }),
            Node("exec", "claude-execute", "Execute", 600, 140,
                new() { ["Prompt"] = "Implement the following plan in this repository:\n\n{{CONTEXT}}", ["AutoCommit"] = "on" }),
            Node("build", "verification", "Build", 880, 140, new() { ["Command"] = "dotnet build" }),
        },
        Edges =
        {
            Edge("start", "Next", "plan", "Execute"),
            Edge("plan", "Next", "exec", "Execute"),
            Edge("exec", "Success", "build", "Execute"),
            Edge("start", "Goal", "plan", "Goal"),
            Edge("plan", "Response", "exec", "Context"),
        }
    };

    private static ScriptGraph BuildEvaluatorOptimizer() => new()
    {
        Nodes =
        {
            Node("start", "start", "Start", 60, 140),
            Node("impl", "claude-execute", "Implement", 300, 140,
                new() { ["Prompt"] = "Implement: {{GOAL}}\n\nIf a fix is provided below, apply it:\n{{CONTEXT}}" }),
            Node("test", "verification", "Test", 580, 140, new() { ["Command"] = "dotnet build" }),
            Node("diag", "gemini-prompt", "Diagnose", 580, 340,
                new() { ["Prompt"] = "Diagnose this build/test failure and give a concrete fix:\n\n{{CONTEXT}}" }),
            Node("loop", "loop-count", "Loop", 860, 340, new() { ["MaxIterations"] = "3" }),
        },
        Edges =
        {
            Edge("start", "Next", "impl", "Execute"),
            Edge("impl", "Success", "test", "Execute"),
            Edge("test", "Failed", "diag", "Execute"),
            Edge("diag", "Next", "loop", "Execute"),
            Edge("loop", "Loop", "impl", "Execute"),
            Edge("start", "Goal", "impl", "Goal"),
            Edge("test", "Response", "diag", "Context"),
            Edge("diag", "Response", "impl", "Context"),
        }
    };

    private static ScriptGraph BuildDialogue() => new()
    {
        Nodes =
        {
            Node("start", "start", "Start", 60, 140),
            Node("debate", "dialogue", "Debate", 320, 140,
                new() { ["Prompt"] = "Debate and converge on the best approach for:\n\n{{GOAL}}", ["Rounds"] = "2" }),
            Node("tasks", "generate-tasks", "Tasks", 620, 140),
        },
        Edges =
        {
            Edge("start", "Next", "debate", "Execute"),
            Edge("debate", "Next", "tasks", "Execute"),
            Edge("start", "Goal", "debate", "Goal"),
            Edge("debate", "Response", "tasks", "Plan"),
        }
    };

    private static ScriptGraph BuildParallelPipeline() => new()
    {
        Nodes =
        {
            Node("start", "start", "Start", 60, 220),
            Node("fork", "parallel", "Fork", 300, 220),
            Node("backend", "claude-execute", "Backend", 540, 100,
                new() { ["Prompt"] = "Implement the backend (APIs, data, tests) for:\n\n{{GOAL}}" }),
            Node("frontend", "gemini-prompt", "Frontend", 540, 340,
                new() { ["Prompt"] = "Draft the frontend components (HTML/CSS/JS) for:\n\n{{GOAL}}" }),
            Node("join", "merge", "Join", 800, 220),
            Node("review", "claude-execute", "Review", 1020, 220,
                new() { ["Prompt"] = "Reconcile the backend and frontend, fixing any type/prop mismatches:\n\n{{CONTEXT}}" }),
        },
        Edges =
        {
            Edge("start", "Next", "fork", "Execute"),
            Edge("fork", "BranchA", "backend", "Execute"),
            Edge("fork", "BranchB", "frontend", "Execute"),
            Edge("backend", "Success", "join", "A"),
            Edge("frontend", "Next", "join", "B"),
            Edge("join", "Next", "review", "Execute"),
            Edge("start", "Goal", "backend", "Goal"),
            Edge("start", "Goal", "frontend", "Goal"),
            Edge("backend", "Response", "review", "Context"),
        }
    };
}
