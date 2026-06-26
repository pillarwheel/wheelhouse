using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IPlanningService"/> by delegating to <see cref="IGeminiService"/>.
/// Registered under the key <c>"Gemini"</c> for keyed DI resolution.
/// </summary>
public class GeminiPlanningService : IPlanningService
{
    private readonly IGeminiService _gemini;

    public GeminiPlanningService(IGeminiService gemini) => _gemini = gemini;

    public bool IsConfigured => _gemini.IsConfigured;

    public Task<string> GenerateResearchPlanAsync(
        string goal,
        string repositoryContext,
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
        => _gemini.GenerateResearchPlanAsync(goal, repositoryContext, cancellationToken);

    public Task<IReadOnlyList<TaskItem>> GenerateTasksAsync(
        string plan,
        CancellationToken cancellationToken = default)
        => _gemini.GenerateTasksAsync(plan, cancellationToken);

    public Task<string> TroubleshootAsync(
        string command,
        string output,
        string repositoryContext,
        CancellationToken cancellationToken = default)
        => _gemini.TroubleshootAsync(command, output, repositoryContext, cancellationToken);
}
