using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

/// <summary>
/// Resolves which planning and orchestration services to use for a given session,
/// based on the session's assigned <see cref="SessionTemplate"/>.
/// Falls back to the default (Gemini + Claude Code) when the session has no template.
/// </summary>
public interface ISessionFlowResolver
{
    IPlanningService GetPlanningService(AgentSession? session = null);
    ITaskOrchestrationService GetOrchestrationService(AgentSession? session = null);
}
