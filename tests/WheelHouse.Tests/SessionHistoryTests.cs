using Microsoft.EntityFrameworkCore;
using WheelHouse.Core;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Persistence;
using Xunit;

namespace WheelHouse.Tests;

public class SessionHistoryTests
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
    public async Task Summary_Projection_Counts_Tasks_Events_And_Workspace()
    {
        using var db = NewDb();
        var ws = new Workspace { Name = "Repo A", AbsolutePath = "/a" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var session = new AgentSession { Name = "S1", WorkspaceId = ws.Id, RepositoryPath = "/a" };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        db.Tasks.AddRange(
            new TaskItem { AgentSessionId = session.Id, Title = "t1", Status = WorkItemStatus.Completed },
            new TaskItem { AgentSessionId = session.Id, Title = "t2", Status = WorkItemStatus.Pending });
        db.SessionEvents.AddRange(
            new SessionEvent { AgentSessionId = session.Id, Kind = "System", Text = "a" },
            new SessionEvent { AgentSessionId = session.Id, Kind = "AssistantText", Text = "b" },
            new SessionEvent { AgentSessionId = session.Id, Kind = "Result", Text = "c" });
        await db.SaveChangesAsync();

        // Mirrors the Sessions page projection.
        var row = await db.Sessions
            .Select(s => new
            {
                s.Id,
                Workspace = s.Workspace != null ? s.Workspace.Name : "—",
                TaskCount = s.Tasks.Count,
                DoneCount = s.Tasks.Count(t => t.Status == WorkItemStatus.Completed),
                EventCount = s.Events.Count
            })
            .SingleAsync();

        Assert.Equal("Repo A", row.Workspace);
        Assert.Equal(2, row.TaskCount);
        Assert.Equal(1, row.DoneCount);
        Assert.Equal(3, row.EventCount);
    }
}
