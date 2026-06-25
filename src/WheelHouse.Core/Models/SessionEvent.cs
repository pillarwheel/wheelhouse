namespace WheelHouse.Core.Models;

/// <summary>
/// A single persisted line of a session's execution transcript (one streamed
/// <see cref="Agents.AgentStreamEvent"/>), so console output and task runs are reviewable
/// after the session is reopened.
/// </summary>
public class SessionEvent
{
    public int Id { get; set; }

    public int AgentSessionId { get; set; }
    public AgentSession? AgentSession { get; set; }

    /// <summary>Task this event belongs to, when produced during a task run.</summary>
    public int? TaskItemId { get; set; }

    /// <summary>Name of the <see cref="Agents.AgentEventKind"/> (stored as text).</summary>
    public string Kind { get; set; } = "System";

    public string Text { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public bool IsError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
