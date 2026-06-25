using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Persistence;
using Xunit;

namespace WheelHouse.Tests;

public class SessionTranscriptTests
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

    private static AgentSession SeedSession(WheelHouseDbContext db)
    {
        var ws = new Workspace { Name = "w", AbsolutePath = "/repo" };
        db.Workspaces.Add(ws);
        db.SaveChanges();
        var session = new AgentSession { Name = "s", WorkspaceId = ws.Id, RepositoryPath = "/repo" };
        db.Sessions.Add(session);
        db.SaveChanges();
        return session;
    }

    [Fact]
    public void Persists_And_Reloads_Transcript_In_Order()
    {
        using var db = NewDb();
        var session = SeedSession(db);

        db.SessionEvents.AddRange(
            new SessionEvent { AgentSessionId = session.Id, Kind = "System", Text = "started" },
            new SessionEvent { AgentSessionId = session.Id, Kind = "AssistantText", Text = "hello" },
            new SessionEvent { AgentSessionId = session.Id, Kind = "Error", Text = "boom", IsError = true });
        db.SaveChanges();

        var loaded = db.SessionEvents
            .Where(e => e.AgentSessionId == session.Id)
            .OrderBy(e => e.Id).ToList();

        Assert.Equal(3, loaded.Count);
        Assert.Equal(new[] { "started", "hello", "boom" }, loaded.Select(e => e.Text));
        Assert.True(loaded[^1].IsError);
    }

    [Fact]
    public void Deleting_Session_Cascades_To_Transcript()
    {
        using var db = NewDb();
        var session = SeedSession(db);
        db.SessionEvents.Add(new SessionEvent { AgentSessionId = session.Id, Kind = "System", Text = "x" });
        db.SaveChanges();

        db.Sessions.Remove(session);
        db.SaveChanges();

        Assert.Empty(db.SessionEvents.Where(e => e.AgentSessionId == session.Id));
    }
}
