using Microsoft.Extensions.DependencyInjection;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Resolves the planning and orchestration services for a session based on its
/// assigned <see cref="SessionTemplate"/>. Currently returns the default Gemini +
/// Claude Code pair; future iterations will dispatch based on template step configuration.
/// </summary>
public class SessionFlowResolver : ISessionFlowResolver
{
    private readonly IServiceProvider _services;

    public SessionFlowResolver(IServiceProvider services) => _services = services;

    public IPlanningService GetPlanningService(AgentSession? session = null)
    {
        var key = ResolveServiceKey(session, FlowStepType.Planning) ?? "Gemini";
        return _services.GetRequiredKeyedService<IPlanningService>(key);
    }

    public ITaskOrchestrationService GetOrchestrationService(AgentSession? session = null)
    {
        var key = ResolveServiceKey(session, FlowStepType.Execute) ?? "ClaudeCode";
        return _services.GetRequiredKeyedService<ITaskOrchestrationService>(key);
    }

    private static string? ResolveServiceKey(AgentSession? session, FlowStepType stepType)
        => session?.Template?.Steps.FirstOrDefault(s => s.StepType == stepType)?.ServiceName;
}
