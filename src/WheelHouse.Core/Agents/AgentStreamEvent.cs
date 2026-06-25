namespace WheelHouse.Core.Agents;

/// <summary>Category of a streamed chunk emitted by the Claude CLI orchestrator.</summary>
public enum AgentEventKind
{
    System,
    AssistantText,
    ToolUse,
    ToolResult,
    Result,
    Error
}

/// <summary>
/// A single normalized chunk parsed from Claude Code's <c>stream-json</c> NDJSON output.
/// </summary>
/// <param name="Kind">The category of event.</param>
/// <param name="Text">Human-readable text for the UI console.</param>
/// <param name="ToolName">Populated for <see cref="AgentEventKind.ToolUse"/> events.</param>
/// <param name="IsError">True when the underlying result reported an error / non-zero exit.</param>
/// <param name="RawJson">The original JSON line, for debugging / replay.</param>
public record AgentStreamEvent(
    AgentEventKind Kind,
    string Text,
    string? ToolName = null,
    bool IsError = false,
    string? RawJson = null);
