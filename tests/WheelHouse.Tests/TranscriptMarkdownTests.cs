using WheelHouse.Core;
using WheelHouse.Core.Export;
using WheelHouse.Core.Models;
using Xunit;

namespace WheelHouse.Tests;

public class TranscriptMarkdownTests
{
    private static AgentSession Session() => new()
    {
        Id = 7,
        Name = "Demo Session",
        RepositoryPath = "/repo",
        Status = SessionStatus.Completed,
        PlanningContext = "1. Do the thing\n2. Verify it",
        CreatedAt = new DateTime(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void Includes_Header_Plan_Tasks_And_Transcript()
    {
        var tasks = new List<TaskItem>
        {
            new() { Sequence = 0, Title = "Add endpoint", Status = WorkItemStatus.Completed,
                    VerificationCommand = "dotnet test", VerificationOutput = "Passed!" }
        };
        var events = new List<SessionEvent>
        {
            new() { Id = 1, Kind = "System", Text = "Started claude" },
            new() { Id = 2, Kind = "AssistantText", Text = "Hello from WheelHouse." },
            new() { Id = 3, Kind = "Error", Text = "boom", IsError = true }
        };

        var md = TranscriptMarkdown.Build(Session(), tasks, events);

        Assert.Contains("# Demo Session", md);
        Assert.Contains("**Repository:** `/repo`", md);
        Assert.Contains("**Status:** Completed", md);
        Assert.Contains("## Plan", md);
        Assert.Contains("Do the thing", md);
        Assert.Contains("### 1. Add endpoint  — Completed", md);
        Assert.Contains("**Verify:** `dotnet test`", md);
        Assert.Contains("Passed!", md);
        Assert.Contains("## Transcript", md);
        Assert.Contains("Hello from WheelHouse.", md);
        Assert.Contains("! boom", md); // error marker
    }

    [Fact]
    public void Handles_Empty_Plan_And_Transcript()
    {
        var md = TranscriptMarkdown.Build(
            new AgentSession { Name = "Empty", RepositoryPath = "/x" },
            Array.Empty<TaskItem>(), Array.Empty<SessionEvent>());

        Assert.Contains("_No plan generated._", md);
        Assert.Contains("_No transcript recorded._", md);
        Assert.DoesNotContain("## Tasks", md); // no tasks section when none
    }

    [Fact]
    public void Fence_Expands_When_Content_Contains_Backticks()
    {
        var events = new List<SessionEvent>
        {
            new() { Id = 1, Kind = "AssistantText", Text = "here is ``` a fence ```" }
        };
        var md = TranscriptMarkdown.Build(Session(), Array.Empty<TaskItem>(), events);
        // Outer fence must be longer than the inner triple backticks.
        Assert.Contains("````text", md);
    }
}
