using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WheelHouse.Core;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Implements <see cref="ITranscriptExportService"/> to export SFT and DPO datasets
/// from historical session transcripts in the SQLite database.
/// </summary>
public class TranscriptExportService : ITranscriptExportService
{
    private readonly WheelHouseDbContext _db;

    public TranscriptExportService(WheelHouseDbContext db)
    {
        _db = db;
    }

    public async Task<string> ExportSftJsonlAsync(CancellationToken cancellationToken = default)
    {
        var tasks = await _db.Tasks
            .Include(t => t.AgentSession)
            .Where(t => t.Status == WorkItemStatus.Completed)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();

        foreach (var task in tasks)
        {
            var events = await _db.SessionEvents
                .Where(e => e.TaskItemId == task.Id && e.Kind == "AssistantText")
                .OrderBy(e => e.Id)
                .ToListAsync(cancellationToken);

            if (events.Count == 0) continue;

            var assistantResponse = CombineEventTexts(events);

            var systemPrompt = "System: You are a software engineer. Your task is to implement coding changes in the repository.";
            var userPrompt = $"Implement the following task in this repository. Make the necessary code changes.\n\nTask: {task.Title}\n\n{task.Description}";
            if (task.AgentSession != null && !string.IsNullOrWhiteSpace(task.AgentSession.PlanningContext))
            {
                userPrompt += $"\n\nOverall plan for context:\n{task.AgentSession.PlanningContext}";
            }

            var item = new SftItem
            {
                messages = new List<ChatMessage>
                {
                    new() { role = "system", content = systemPrompt },
                    new() { role = "user", content = userPrompt },
                    new() { role = "assistant", content = assistantResponse }
                }
            };

            var json = JsonSerializer.Serialize(item);
            sb.AppendLine(json);
        }

        return sb.ToString();
    }

    public async Task<string> ExportDpoJsonlAsync(CancellationToken cancellationToken = default)
    {
        var tasks = await _db.Tasks
            .Include(t => t.AgentSession)
            .Where(t => t.Status == WorkItemStatus.Completed)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();

        foreach (var task in tasks)
        {
            var events = await _db.SessionEvents
                .Where(e => e.TaskItemId == task.Id)
                .OrderBy(e => e.Id)
                .ToListAsync(cancellationToken);

            if (events.Count == 0) continue;

            // Find the first failure event (Error or failing exit result)
            var failureIndex = events.FindIndex(e => e.IsError || e.Kind == "Error" || (e.Kind == "Result" && e.Text.Contains("exit") && !e.Text.Contains("exit 0")));
            if (failureIndex == -1) continue;

            // rejectedContent = all AssistantTexts before the first failure
            var rejectedEvents = events
                .Take(failureIndex)
                .Where(e => e.Kind == "AssistantText")
                .ToList();

            // chosenContent = all AssistantTexts after the first failure
            var chosenEvents = events
                .Skip(failureIndex + 1)
                .Where(e => e.Kind == "AssistantText")
                .ToList();

            if (rejectedEvents.Count == 0 || chosenEvents.Count == 0) continue;

            var rejectedContent = CombineEventTexts(rejectedEvents);
            var chosenContent = CombineEventTexts(chosenEvents);

            var promptText = $"Implement the following task in this repository. Make the necessary code changes.\n\nTask: {task.Title}\n\n{task.Description}";
            if (task.AgentSession != null && !string.IsNullOrWhiteSpace(task.AgentSession.PlanningContext))
            {
                promptText += $"\n\nOverall plan for context:\n{task.AgentSession.PlanningContext}";
            }

            var item = new DpoItem
            {
                prompt = promptText,
                chosen = new List<ChatMessage>
                {
                    new() { role = "assistant", content = chosenContent }
                },
                rejected = new List<ChatMessage>
                {
                    new() { role = "assistant", content = rejectedContent }
                }
            };

            var json = JsonSerializer.Serialize(item);
            sb.AppendLine(json);
        }

        return sb.ToString();
    }

    private static string CombineEventTexts(IEnumerable<SessionEvent> events)
    {
        var builder = new StringBuilder();
        foreach (var ev in events)
        {
            if (!string.IsNullOrWhiteSpace(ev.Text))
            {
                builder.AppendLine(ev.Text);
            }
        }
        return builder.ToString().Trim();
    }

    private class ChatMessage
    {
        public string role { get; set; } = string.Empty;
        public string content { get; set; } = string.Empty;
    }

    private class SftItem
    {
        public List<ChatMessage> messages { get; set; } = new();
    }

    private class DpoItem
    {
        public string prompt { get; set; } = string.Empty;
        public List<ChatMessage> chosen { get; set; } = new();
        public List<ChatMessage> rejected { get; set; } = new();
    }
}
