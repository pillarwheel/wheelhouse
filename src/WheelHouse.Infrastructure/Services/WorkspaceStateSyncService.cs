using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WheelHouse.Core;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Service that automatically synchronizes active session state (plan, tasks, status)
/// to the workspace repository filesystem under the .wheelhouse folder (GitOps).
/// </summary>
public class WorkspaceStateSyncService : IWorkspaceStateSyncService
{
    private readonly WheelHouseDbContext _db;
    private readonly ILogger<WorkspaceStateSyncService> _logger;

    public WorkspaceStateSyncService(WheelHouseDbContext db, ILogger<WorkspaceStateSyncService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SyncActiveSessionAsync(int sessionId, string repositoryPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
            {
                _logger.LogWarning("Cannot sync active session state: repository path '{Path}' does not exist.", repositoryPath);
                return;
            }

            var session = await _db.Sessions
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (session is null)
            {
                _logger.LogWarning("Cannot sync active session state: Session ID {Id} not found in database.", sessionId);
                return;
            }

            var tasks = await _db.Tasks
                .Where(t => t.AgentSessionId == sessionId)
                .OrderBy(t => t.Sequence)
                .ToListAsync(cancellationToken);

            var dotWheelhouseDir = Path.Combine(repositoryPath, ".wheelhouse");
            Directory.CreateDirectory(dotWheelhouseDir);

            // 1. Sync plan.md
            var planPath = Path.Combine(dotWheelhouseDir, "plan.md");
            var planContent = new StringBuilder();
            planContent.AppendLine("# Active Implementation Plan");
            planContent.AppendLine();
            planContent.AppendLine(session.PlanningContext ?? "*No plan generated yet.*");
            await File.WriteAllTextAsync(planPath, planContent.ToString(), Encoding.UTF8, cancellationToken);

            // 2. Sync tasks.md
            var tasksPath = Path.Combine(dotWheelhouseDir, "tasks.md");
            var tasksContent = new StringBuilder();
            tasksContent.AppendLine("# Tasks Checklist");
            tasksContent.AppendLine();
            if (tasks.Count == 0)
            {
                tasksContent.AppendLine("*No tasks generated yet.*");
            }
            else
            {
                foreach (var task in tasks)
                {
                    var checkbox = task.Status switch
                    {
                        WorkItemStatus.Completed => "[x]",
                        WorkItemStatus.InProgress => "[/]",
                        WorkItemStatus.Verifying => "[/]",
                        WorkItemStatus.Failed => "[!]",
                        _ => "[ ]"
                    };

                    var riskTag = task.Risk != RiskLevel.Low ? $" — _{task.Risk} risk_" : "";
                    tasksContent.AppendLine($"- {checkbox} **{task.Title}** ({task.Status}){riskTag}");
                    if (!string.IsNullOrWhiteSpace(task.Description))
                    {
                        tasksContent.AppendLine($"  - *Description*: {task.Description}");
                    }
                    if (!string.IsNullOrWhiteSpace(task.SkillTags))
                    {
                        tasksContent.AppendLine($"  - *Skills*: {task.SkillTags}");
                    }
                    if (!string.IsNullOrWhiteSpace(task.VerificationCommand))
                    {
                        tasksContent.AppendLine($"  - *Verification*: `{task.VerificationCommand}`");
                    }
                    if (!string.IsNullOrWhiteSpace(task.VerificationOutput))
                    {
                        tasksContent.AppendLine("  - *Output*:");
                        tasksContent.AppendLine("    ```");
                        // Indent output lines
                        foreach (var line in task.VerificationOutput.Split('\n'))
                        {
                            tasksContent.AppendLine($"    {line}");
                        }
                        tasksContent.AppendLine("    ```");
                    }
                    tasksContent.AppendLine();
                }
            }
            await File.WriteAllTextAsync(tasksPath, tasksContent.ToString(), Encoding.UTF8, cancellationToken);

            // 3. Sync status.md
            var statusPath = Path.Combine(dotWheelhouseDir, "status.md");
            var statusContent = new StringBuilder();
            var activeTask = tasks.FirstOrDefault(t => t.Status is WorkItemStatus.InProgress or WorkItemStatus.Verifying);

            statusContent.AppendLine("# Session Status");
            statusContent.AppendLine();
            statusContent.AppendLine($"- **Session Name**: {session.Name}");
            statusContent.AppendLine($"- **Session ID**: {session.SessionId}");
            statusContent.AppendLine($"- **Status**: {session.Status}");
            statusContent.AppendLine($"- **Started At**: {session.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            if (session.CompletedAt.HasValue)
            {
                statusContent.AppendLine($"- **Completed At**: {session.CompletedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            }
            statusContent.AppendLine($"- **Repository Path**: `{session.RepositoryPath}`");
            statusContent.AppendLine($"- **Active Task**: {(activeTask != null ? $"**{activeTask.Title}**" : "*None*")}");
            statusContent.AppendLine($"- **Last Updated**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            await File.WriteAllTextAsync(statusPath, statusContent.ToString(), Encoding.UTF8, cancellationToken);

            // 4. Sync handoff.md — momentum queues so any compatible agent can continue
            //    from the folder alone (now / next / blocked / awaiting-approval).
            var handoffPath = Path.Combine(dotWheelhouseDir, "handoff.md");
            var handoff = new StringBuilder();
            handoff.AppendLine("# Handoff");
            handoff.AppendLine();
            handoff.AppendLine($"_Goal_: {session.Name}");
            handoff.AppendLine($"_Session status_: {session.Status} · _Last updated_: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            handoff.AppendLine();

            void AppendQueue(string heading, IEnumerable<TaskItem> items, string emptyText)
            {
                handoff.AppendLine($"## {heading}");
                var any = false;
                foreach (var t in items)
                {
                    any = true;
                    var risk = t.Risk != RiskLevel.Low ? $" ({t.Risk} risk)" : "";
                    handoff.AppendLine($"- **{t.Title}**{risk}");
                }
                if (!any) handoff.AppendLine($"_{emptyText}_");
                handoff.AppendLine();
            }

            AppendQueue("Now (active)",
                tasks.Where(t => t.Status is WorkItemStatus.InProgress or WorkItemStatus.Verifying),
                "Nothing in progress.");
            AppendQueue("Next (ready)",
                tasks.Where(t => t.Status == WorkItemStatus.Pending),
                "No pending tasks.");
            AppendQueue("Awaiting approval",
                tasks.Where(t => t.Status == WorkItemStatus.AwaitingApproval),
                "None waiting on a human.");
            AppendQueue("Blocked (failed)",
                tasks.Where(t => t.Status == WorkItemStatus.Failed),
                "No failed tasks.");

            handoff.AppendLine("## Continuation");
            if (activeTask is not null)
                handoff.AppendLine($"Resume the active task **{activeTask.Title}**, then proceed through the Next queue in order.");
            else if (tasks.Any(t => t.Status == WorkItemStatus.Pending))
                handoff.AppendLine("Start the first task in the Next queue.");
            else if (tasks.Any(t => t.Status == WorkItemStatus.AwaitingApproval))
                handoff.AppendLine("A human approval is required before continuing.");
            else
                handoff.AppendLine("No outstanding tasks. Review `knowledge.md` and define the next milestone.");

            await File.WriteAllTextAsync(handoffPath, handoff.ToString(), Encoding.UTF8, cancellationToken);

            _logger.LogInformation("Successfully synchronized session {SessionId} state to {Dir}", sessionId, dotWheelhouseDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error synchronizing session {SessionId} state to workspace.", sessionId);
        }
    }
}
