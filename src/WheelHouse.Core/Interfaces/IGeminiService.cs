using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

/// <summary>
/// Wraps Gemini for research, plan/task generation, troubleshooting and embeddings.
/// </summary>
public interface IGeminiService
{
    /// <summary>True when an API key is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Runs an arbitrary, fully-formed prompt (e.g. a rendered template) and returns the response.</summary>
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>Produces a markdown research/implementation plan for a goal.</summary>
    Task<string> GenerateResearchPlanAsync(
        string goal,
        string repositoryContext,
        CancellationToken cancellationToken = default);

    /// <summary>Breaks a plan into ordered, verifiable task items.</summary>
    Task<IReadOnlyList<TaskItem>> GenerateTasksAsync(
        string plan,
        CancellationToken cancellationToken = default);

    /// <summary>Suggests a fix given a failing command and its output.</summary>
    Task<string> TroubleshootAsync(
        string command,
        string output,
        string repositoryContext,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an embedding vector for the given text.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
