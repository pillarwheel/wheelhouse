using Microsoft.EntityFrameworkCore;
using WheelHouse.Core;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class TranscriptExportServiceTests
{
    private static WheelHouseDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
            .UseSqlite($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;
        var db = new WheelHouseDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task ExportSftJsonl_Aggregates_Completed_Tasks()
    {
        // Arrange
        using var db = NewDb();
        var ws = new Workspace { Name = "Test Repo", AbsolutePath = "/test" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var session = new AgentSession { Name = "SftSession", WorkspaceId = ws.Id, RepositoryPath = "/test", PlanningContext = "Planning info" };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var task = new TaskItem { AgentSessionId = session.Id, Title = "Task 1", Description = "Desc 1", Status = WorkItemStatus.Completed };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        db.SessionEvents.AddRange(
            new SessionEvent { AgentSessionId = session.Id, TaskItemId = task.Id, Kind = "AssistantText", Text = "Assistant message 1" },
            new SessionEvent { AgentSessionId = session.Id, TaskItemId = task.Id, Kind = "AssistantText", Text = "Assistant message 2" }
        );
        await db.SaveChangesAsync();

        var exporter = new TranscriptExportService(db);

        // Act
        var result = await exporter.ExportSftJsonlAsync();

        // Assert
        Assert.Contains("Assistant message 1", result);
        Assert.Contains("Assistant message 2", result);
        Assert.Contains("Planning info", result);
        Assert.Contains("Task 1", result);
    }

    [Fact]
    public async Task ExportDpoJsonl_Groups_Chosen_And_Rejected_Pairs()
    {
        // Arrange
        using var db = NewDb();
        var ws = new Workspace { Name = "Test Repo", AbsolutePath = "/test" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var session = new AgentSession { Name = "DpoSession", WorkspaceId = ws.Id, RepositoryPath = "/test" };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var task = new TaskItem { AgentSessionId = session.Id, Title = "Task 2", Description = "Desc 2", Status = WorkItemStatus.Completed };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        db.SessionEvents.AddRange(
            // Attempt 1: fails
            new SessionEvent { AgentSessionId = session.Id, TaskItemId = task.Id, Kind = "AssistantText", Text = "Rejected solution" },
            new SessionEvent { AgentSessionId = session.Id, TaskItemId = task.Id, Kind = "Result", Text = "Verification failed (exit 1)", IsError = true },
            // Attempt 2: succeeds
            new SessionEvent { AgentSessionId = session.Id, TaskItemId = task.Id, Kind = "AssistantText", Text = "Chosen solution" },
            new SessionEvent { AgentSessionId = session.Id, TaskItemId = task.Id, Kind = "Result", Text = "exit 0", IsError = false }
        );
        await db.SaveChangesAsync();

        var exporter = new TranscriptExportService(db);

        // Act
        var result = await exporter.ExportDpoJsonlAsync();

        // Assert
        Assert.Contains("Rejected solution", result);
        Assert.Contains("Chosen solution", result);
        Assert.Contains("Task 2", result);
    }
}
