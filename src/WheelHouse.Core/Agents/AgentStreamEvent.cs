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
/// Real token/cost/duration accounting reported by Claude Code's final <c>result</c> message.
/// </summary>
/// <param name="InputTokens">Prompt-side tokens, including cache reads/writes.</param>
/// <param name="OutputTokens">Generated tokens.</param>
/// <param name="DurationMs">Wall-clock duration of the run as reported by the CLI.</param>
/// <param name="CostUsd">Total cost in USD when the CLI reports it (API-key runs; null on subscription).</param>
public record AgentUsage(int InputTokens, int OutputTokens, long DurationMs, double? CostUsd)
{
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// A single normalized chunk parsed from Claude Code's <c>stream-json</c> NDJSON output.
/// </summary>
/// <param name="Kind">The category of event.</param>
/// <param name="Text">Human-readable text for the UI console.</param>
/// <param name="ToolName">Populated for <see cref="AgentEventKind.ToolUse"/> events.</param>
/// <param name="IsError">True when the underlying result reported an error / non-zero exit.</param>
/// <param name="RawJson">The original JSON line, for debugging / replay.</param>
/// <param name="Usage">Populated on the final <see cref="AgentEventKind.Result"/> event when available.</param>
public record AgentStreamEvent(
    AgentEventKind Kind,
    string Text,
    string? ToolName = null,
    bool IsError = false,
    string? RawJson = null,
    AgentUsage? Usage = null);
