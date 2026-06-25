using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Models;
using WheelHouse.Core.Search;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class TranscriptSnippetTests
{
    [Fact]
    public void Centres_On_Match_With_Ellipses()
    {
        var text = new string('a', 100) + " NEEDLE " + new string('b', 100);
        var snip = TranscriptSnippet.Build(text, "needle", radius: 10);
        Assert.Contains("NEEDLE", snip);
        Assert.StartsWith("… ", snip);
        Assert.EndsWith(" …", snip);
    }

    [Fact]
    public void Collapses_Newlines_To_Single_Line()
    {
        var snip = TranscriptSnippet.Build("line one\nline two NEEDLE here", "needle", radius: 50);
        Assert.DoesNotContain("\n", snip);
        Assert.Contains("NEEDLE", snip);
    }

    [Fact]
    public void No_Match_Returns_Leading_Text()
    {
        var snip = TranscriptSnippet.Build("short text", "zzz", radius: 60);
        Assert.Equal("short text", snip);
    }
}

public class TranscriptSearchServiceTests
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

    private static int SeedSession(WheelHouseDbContext db, string name, params string[] eventTexts)
    {
        var ws = new Workspace { Name = "w", AbsolutePath = "/repo-" + Guid.NewGuid().ToString("N") };
        db.Workspaces.Add(ws);
        db.SaveChanges();
        var session = new AgentSession { Name = name, WorkspaceId = ws.Id, RepositoryPath = ws.AbsolutePath };
        db.Sessions.Add(session);
        db.SaveChanges();
        foreach (var t in eventTexts)
            db.SessionEvents.Add(new SessionEvent { AgentSessionId = session.Id, Kind = "AssistantText", Text = t });
        db.SaveChanges();
        return session.Id;
    }

    [Fact]
    public async Task Finds_Matching_Session_Case_Insensitively()
    {
        using var db = NewDb();
        var s1 = SeedSession(db, "Auth work", "Implemented JWT authentication", "added login form");
        SeedSession(db, "Payments", "stripe billing integration");

        var results = await new TranscriptSearchService(db).SearchAsync("AUTHENTICATION");

        Assert.Single(results);
        Assert.Equal(s1, results[0].SessionId);
        Assert.Equal("Auth work", results[0].SessionName);
        Assert.Equal(1, results[0].MatchCount);
        Assert.Contains("authentication", results[0].Hits[0].Snippet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Groups_Multiple_Hits_Per_Session()
    {
        using var db = NewDb();
        SeedSession(db, "S", "error: boom", "another error here", "all good");

        var results = await new TranscriptSearchService(db).SearchAsync("error");

        Assert.Single(results);
        Assert.Equal(2, results[0].MatchCount);
    }

    [Fact]
    public async Task Empty_Query_Returns_Nothing()
    {
        using var db = NewDb();
        SeedSession(db, "S", "something");
        Assert.Empty(await new TranscriptSearchService(db).SearchAsync("   "));
    }

    [Fact]
    public async Task Wildcards_Are_Matched_Literally()
    {
        using var db = NewDb();
        SeedSession(db, "Literal", "progress is 100% complete");
        SeedSession(db, "Other", "nothing relevant");

        // '%' must be treated literally, not as a LIKE wildcard.
        var results = await new TranscriptSearchService(db).SearchAsync("100%");
        Assert.Single(results);
        Assert.Equal("Literal", results[0].SessionName);
    }
}
