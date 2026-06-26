using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

/// <summary>
/// Abstracts the planning/research service used by a session flow (Gemini by default).
/// Swappable per-session via <see cref="ISessionFlowResolver"/>.
/// </summary>
public interface IPlanningService
{
    bool IsConfigured { get; }

    Task<string> GenerateResearchPlanAsync(
        string goal,
        string repositoryContext,
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskItem>> GenerateTasksAsync(
        string plan,
        CancellationToken cancellationToken = default);

    Task<string> TroubleshootAsync(
        string command,
        string output,
        string repositoryContext,
        CancellationToken cancellationToken = default);
}
