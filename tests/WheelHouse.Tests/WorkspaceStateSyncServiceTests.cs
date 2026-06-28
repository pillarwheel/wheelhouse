using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Core;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class WorkspaceStateSyncServiceTests : IDisposable
{
    private readonly string _tempRepoDir;
    private readonly WheelHouseDbContext _db;

    public WorkspaceStateSyncServiceTests()
    {
        // Setup temp workspace directory
        _tempRepoDir = Path.Combine(Path.GetTempPath(), "wheelhouse-test-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRepoDir);

        // Setup in-memory database
        var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
            .UseSqlite($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;
        _db = new WheelHouseDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();

        if (Directory.Exists(_tempRepoDir))
        {
            Directory.Delete(_tempRepoDir, true);
        }
    }

    [Fact]
    public async Task SyncActiveSession_WritesCorrectFilesToDisk()
    {
        // Arrange
        var ws = new Workspace { Name = "Test WS", AbsolutePath = _tempRepoDir };
        _db.Workspaces.Add(ws);
        await _db.SaveChangesAsync();

        var session = new AgentSession
        {
            Name = "Sync Session Test",
            WorkspaceId = ws.Id,
            RepositoryPath = _tempRepoDir,
            Status = SessionStatus.Running,
            PlanningContext = "# Test Plan\n- Step 1\n- Step 2"
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var tasks = new[]
        {
            new TaskItem
            {
                AgentSessionId = session.Id,
                Sequence = 0,
                Title = "Task 1",
                Description = "Desc 1",
                VerificationCommand = "dotnet build",
                Status = WorkItemStatus.Completed
            },
            new TaskItem
            {
                AgentSessionId = session.Id,
                Sequence = 1,
                Title = "Task 2",
                Description = "Desc 2",
                VerificationCommand = "dotnet test",
                Status = WorkItemStatus.InProgress
            },
            new TaskItem
            {
                AgentSessionId = session.Id,
                Sequence = 2,
                Title = "Task 3",
                Status = WorkItemStatus.Pending
            }
        };
        _db.Tasks.AddRange(tasks);
        await _db.SaveChangesAsync();

        var syncService = new WorkspaceStateSyncService(_db, NullLogger<WorkspaceStateSyncService>.Instance);

        // Act
        await syncService.SyncActiveSessionAsync(session.Id, _tempRepoDir);

        // Assert
        var dotWheelhouse = Path.Combine(_tempRepoDir, ".wheelhouse");
        Assert.True(Directory.Exists(dotWheelhouse));

        var planFile = Path.Combine(dotWheelhouse, "plan.md");
        Assert.True(File.Exists(planFile));
        var planContent = await File.ReadAllTextAsync(planFile);
        Assert.Contains("# Test Plan", planContent);

        var tasksFile = Path.Combine(dotWheelhouse, "tasks.md");
        Assert.True(File.Exists(tasksFile));
        var tasksContent = await File.ReadAllTextAsync(tasksFile);
        Assert.Contains("- [x] **Task 1**", tasksContent);
        Assert.Contains("- [/] **Task 2**", tasksContent);
        Assert.Contains("- [ ] **Task 3**", tasksContent);
        Assert.Contains("*Verification*: `dotnet build`", tasksContent);

        var statusFile = Path.Combine(dotWheelhouse, "status.md");
        Assert.True(File.Exists(statusFile));
        var statusContent = await File.ReadAllTextAsync(statusFile);
        Assert.Contains("**Session Name**: Sync Session Test", statusContent);
        Assert.Contains("**Status**: Running", statusContent);
        Assert.Contains("**Active Task**: **Task 2**", statusContent);
    }
}
