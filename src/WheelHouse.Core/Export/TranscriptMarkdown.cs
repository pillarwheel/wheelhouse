using System.Text;
using WheelHouse.Core.Models;

namespace WheelHouse.Core.Export;

/// <summary>
/// Renders a session (metadata + Gemini plan + Test-Driven tasks + execution transcript) to a
/// self-contained Markdown report. Pure and deterministic so it can be unit-tested.
/// </summary>
public static class TranscriptMarkdown
{
    public static string Build(
        AgentSession session,
        IReadOnlyList<TaskItem> tasks,
        IReadOnlyList<SessionEvent> events)
    {
        var sb = new StringBuilder();

        sb.Append("# ").AppendLine(session.Name).AppendLine();
        sb.Append("- **Repository:** `").Append(session.RepositoryPath).AppendLine("`");
        sb.Append("- **Status:** ").AppendLine(session.Status.ToString());
        sb.Append("- **Created:** ").AppendLine(session.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        sb.Append("- **Completed:** ").AppendLine(
            session.CompletedAt is { } c ? c.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—");
        sb.Append("- **Exported:** ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine();

        sb.AppendLine("## Plan").AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(session.PlanningContext)
            ? "_No plan generated._"
            : session.PlanningContext!.Trim());
        sb.AppendLine();

        if (tasks.Count > 0)
        {
            sb.AppendLine("## Tasks").AppendLine();
            foreach (var t in tasks.OrderBy(t => t.Sequence))
            {
                sb.Append("### ").Append(t.Sequence + 1).Append(". ").Append(t.Title)
                    .Append("  — ").AppendLine(t.Status.ToString());
                if (!string.IsNullOrWhiteSpace(t.Description))
                    sb.AppendLine().AppendLine(t.Description.Trim());
                if (!string.IsNullOrWhiteSpace(t.VerificationCommand))
                    sb.AppendLine().Append("**Verify:** `").Append(t.VerificationCommand).AppendLine("`");
                if (!string.IsNullOrWhiteSpace(t.VerificationOutput))
                {
                    var fence = Fence(t.VerificationOutput!);
                    sb.AppendLine().AppendLine(fence)
                        .AppendLine(t.VerificationOutput!.TrimEnd()).AppendLine(fence);
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Transcript").AppendLine();
        if (events.Count == 0)
        {
            sb.AppendLine("_No transcript recorded._");
        }
        else
        {
            var body = new StringBuilder();
            foreach (var e in events.OrderBy(e => e.Id))
            {
                var ts = e.CreatedAt.ToLocalTime().ToString("HH:mm:ss");
                var prefix = $"[{ts}] {Label(e.Kind),-6} ";
                var pad = new string(' ', prefix.Length);
                var lines = (e.Text ?? string.Empty).Replace("\r", "").Split('\n');
                body.Append(prefix).Append(e.IsError ? "! " : "").AppendLine(lines[0]);
                for (var i = 1; i < lines.Length; i++) body.Append(pad).AppendLine(lines[i]);
            }
            var fence = Fence(body.ToString());
            sb.Append(fence).AppendLine("text")
                .AppendLine(body.ToString().TrimEnd())
                .AppendLine(fence);
        }

        return sb.ToString();
    }

    private static string Label(string kind) => kind switch
    {
        "AssistantText" => "claude",
        "ToolUse" => "tool",
        "ToolResult" => "result",
        "System" => "sys",
        "Result" => "done",
        "Error" => "error",
        _ => "·"
    };

    /// <summary>Returns a backtick fence longer than the longest backtick run in the content.</summary>
    private static string Fence(string content)
    {
        int max = 0, run = 0;
        foreach (var ch in content)
        {
            if (ch == '`') { run++; if (run > max) max = run; }
            else run = 0;
        }
        return new string('`', Math.Max(3, max + 1));
    }
}
