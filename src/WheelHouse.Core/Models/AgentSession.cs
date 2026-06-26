namespace WheelHouse.Core.Models;

/// <summary>
/// An active or historic orchestration session: Gemini plans the work, Claude Code executes it.
/// </summary>
public class AgentSession
{
    public int Id { get; set; }

    /// <summary>Stable identifier (Claude session id when available, otherwise a GUID).</summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public SessionStatus Status { get; set; } = SessionStatus.Planning;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Repository path this session operates on (snapshot of the workspace path).</summary>
    public string RepositoryPath { get; set; } = string.Empty;

    /// <summary>Gemini-produced research/planning context (markdown).</summary>
    public string? PlanningContext { get; set; }

    public int WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    /// <summary>Optional template controlling which planning/execution services are used. Null falls back to the default flow.</summary>
    public int? TemplateId { get; set; }
    public SessionTemplate? Template { get; set; }

    public List<TaskItem> Tasks { get; set; } = new();
    public List<SessionEvent> Events { get; set; } = new();
}
