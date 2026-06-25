using Microsoft.EntityFrameworkCore;
using WheelHouse.Core;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure;
using WheelHouse.Infrastructure.Persistence;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// Gated seeder (WHEELHOUSE_SEED=1) that creates a scratch workspace + session + one task with a
/// deterministic verification command, for the manual full-loop browser walkthrough. Writes the new
/// session id to %TEMP%/wh_fullloop_id.txt.
/// </summary>
public class FullLoopSeed
{
    [Fact]
    public async Task Seed()
    {
        if (Environment.GetEnvironmentVariable("WHEELHOUSE_SEED") is not ("1" or "true")) return;

        var scratch = Path.Combine(Path.GetTempPath(), "wh_fullloop");
        if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
        Directory.CreateDirectory(scratch);

        var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
            .UseSqlite($"Data Source={DependencyInjection.DefaultDatabasePath()}")
            .Options;
        using var db = new WheelHouseDbContext(options);

        var ws = new Workspace { Name = "FullLoop Demo", AbsolutePath = scratch, PermissionMode = "acceptEdits" };
        db.Workspaces.Add(ws);
        await db.SaveChangesAsync();

        var session = new AgentSession
        {
            Name = "Full Loop Verification",
            WorkspaceId = ws.Id,
            RepositoryPath = scratch,
            Status = SessionStatus.Planning
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        db.Tasks.Add(new TaskItem
        {
            AgentSessionId = session.Id,
            Sequence = 0,
            Status = WorkItemStatus.Pending,
            Title = "Create result.txt with PASS",
            Description = "Create a file named result.txt in the current working directory. " +
                          "Its entire contents must be exactly the word PASS with no other text.",
            VerificationCommand =
                "if ((Test-Path 'result.txt') -and ((Get-Content 'result.txt' -Raw).Trim() -eq 'PASS')) " +
                "{ Write-Output 'VERIFIED'; exit 0 } else { Write-Error 'result.txt missing or wrong'; exit 1 }"
        });
        await db.SaveChangesAsync();

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "wh_fullloop_id.txt"), session.Id.ToString());
    }
}
