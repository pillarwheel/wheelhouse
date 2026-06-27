using Microsoft.Extensions.DependencyInjection;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Resolves the planning and orchestration services for a session based on its
/// assigned <see cref="SessionTemplate"/>. Falls back to the default Gemini +
/// Claude Code pair when the session has no template, or when a configured
/// service name does not match a registered implementation.
/// </summary>
public class SessionFlowResolver : ISessionFlowResolver
{
    private const string DefaultPlanningKey = "Gemini";
    private const string DefaultOrchestrationKey = "ClaudeCode";

    private readonly IServiceProvider _services;

    public SessionFlowResolver(IServiceProvider services) => _services = services;

    public IPlanningService GetPlanningService(AgentSession? session = null)
    {
        var key = ResolveServiceKey(session, FlowStepType.Planning);
        var resolved = key is null ? null : _services.GetKeyedService<IPlanningService>(key);
        return resolved ?? _services.GetRequiredKeyedService<IPlanningService>(DefaultPlanningKey);
    }

    public ITaskOrchestrationService GetOrchestrationService(AgentSession? session = null)
    {
        // A single orchestration service backs Task/Execute/Verify; prefer the Execute
        // step's configuration but fall back to the other orchestration steps.
        var key = ResolveServiceKey(session, FlowStepType.Execute)
                  ?? ResolveServiceKey(session, FlowStepType.Task)
                  ?? ResolveServiceKey(session, FlowStepType.Verify);
        var resolved = key is null ? null : _services.GetKeyedService<ITaskOrchestrationService>(key);
        return resolved ?? _services.GetRequiredKeyedService<ITaskOrchestrationService>(DefaultOrchestrationKey);
    }

    private static string? ResolveServiceKey(AgentSession? session, FlowStepType stepType)
        => session?.Template?.Steps.FirstOrDefault(s => s.StepType == stepType)?.ServiceName;
}
