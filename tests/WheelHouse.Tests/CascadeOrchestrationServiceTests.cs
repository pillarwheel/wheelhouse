using System.Text;
using WheelHouse.Core;
using WheelHouse.Core.Agents;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class CascadeOrchestrationServiceTests : IDisposable
{
    private readonly FakeClaudeService _claudeService = new();
    private readonly FakeGeminiService _geminiService = new();
    private readonly FakeGitService _gitService = new();
    private readonly FakeVerificationRunner _verificationRunner = new();
    private readonly FakeSearchService _searchService = new();
    private readonly CascadeOptions _options = new();
    private readonly CascadeOrchestrationService _service;
    private readonly string _workspace;

    public CascadeOrchestrationServiceTests()
    {
        _service = new CascadeOrchestrationService(
            _claudeService, _geminiService, _gitService, _verificationRunner, _searchService, _options);
        _workspace = Path.Combine(Path.GetTempPath(), "wheelhouse-cascade-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        TryDelete(Path.Combine(Path.GetTempPath(), "wheelhouse-cascade-tests"));
        foreach (var worktree in _gitService.AddedWorktrees)
        {
            TryDelete(worktree.Path);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    [Fact]
    public async Task Cascade_Off_Switch_Routes_Straight_To_Claude()
    {
        _options.Mode = "off";
        var request = new AgentRunRequest("Fix bug", _workspace, VerificationCommand: "dotnet test");

        var events = await ToListAsync(_service.RunAsync(request));

        Assert.Contains(events, e => e.Text.Contains("WHEELHOUSE_CASCADE=off"));
        Assert.Empty(_geminiService.Prompts); // cheap tier never ran
        Assert.Single(_claudeService.Runs);
    }

    [Fact]
    public async Task Rag_Shortlist_Grounds_The_Identify_Step()
    {
        // The index knows about src/Payment.cs; the identify prompt should carry it.
        Directory.CreateDirectory(Path.Combine(_workspace, "src"));
        _searchService.Hits.Add(new CodeSearchResult(
            new CodeIndexEntry { FilePath = Path.Combine(_workspace, "src", "Payment.cs") }, 0.9));

        var request = new AgentRunRequest("Fix billing bug", _workspace, VerificationCommand: "dotnet test");
        _geminiService.Responses.Enqueue("not json at all — unusable");
        _geminiService.Responses.Enqueue("FILE: src/Payment.cs\nCONTENT:\nfixed\nEND_FILE");
        _verificationRunner.Result = new VerificationResult(0, "Passed", false);

        var events = await ToListAsync(_service.RunAsync(request));

        Assert.Contains("Payment.cs", _geminiService.Prompts[0]); // shortlist reached the model
        Assert.Contains(events, e => e.Text.Contains("shortlisted 1 candidate"));
    }

    [Fact]
    public async Task Cheap_Tier_Emits_Estimated_Usage_Telemetry()
    {
        var request = new AgentRunRequest("Fix bug", _workspace, VerificationCommand: "dotnet test");
        _geminiService.Responses.Enqueue("[\"src/Temp.cs\"]");
        _geminiService.Responses.Enqueue("FILE: src/Temp.cs\nCONTENT:\npublic class Temp {}\nEND_FILE");
        _verificationRunner.Result = new VerificationResult(0, "Passed", false);

        var events = await ToListAsync(_service.RunAsync(request));

        var usage = events.Single(e => e.Usage is not null).Usage!;
        Assert.True(usage.TotalTokens > 0);
        Assert.Null(usage.CostUsd); // estimated tokens, no invented dollars
    }

    [Fact]
    public async Task Routes_Directly_To_Claude_If_No_VerificationCommand()
    {
        // Arrange
        var request = new AgentRunRequest("Do something", _workspace);
        _claudeService.Events.Add(new AgentStreamEvent(AgentEventKind.AssistantText, "Claude output"));

        // Act
        var events = await ToListAsync(_service.RunAsync(request));

        // Assert
        Assert.Contains(events, e => e.Text == "Claude output");
        Assert.Empty(_geminiService.Prompts);
        Assert.Single(_claudeService.Runs);
    }

    [Fact]
    public async Task Cascade_Succeeds_Without_Claude_If_Verification_Passes()
    {
        // Arrange (non-repo workspace: edits land directly in the workspace)
        var request = new AgentRunRequest("Fix bug", _workspace, VerificationCommand: "dotnet test");
        _geminiService.Responses.Enqueue("[\"src/Temp.cs\"]"); // Step 1: files
        _geminiService.Responses.Enqueue("FILE: src/Temp.cs\nCONTENT:\npublic class Temp {}\nEND_FILE"); // Step 2: changes
        _verificationRunner.Result = new VerificationResult(0, "Passed", false);

        // Act
        var events = await ToListAsync(_service.RunAsync(request));

        // Assert
        Assert.Contains(events, e => e.Text.Contains("Cascade: Verification passed. Cheap tier implementation succeeded."));
        Assert.Empty(_claudeService.Runs);
        Assert.Empty(_gitService.DiscardedPaths);
        Assert.True(File.Exists(Path.Combine(_workspace, "src", "Temp.cs")));
    }

    [Fact]
    public async Task Reverts_Only_CheapTier_Files_And_Escalates_To_Claude_If_Verification_Fails()
    {
        // Arrange (non-repo workspace: rollback must remove exactly what the cheap tier created)
        var request = new AgentRunRequest("Fix bug", _workspace, VerificationCommand: "dotnet test");
        _geminiService.Responses.Enqueue("[\"src/Temp.cs\"]");
        _geminiService.Responses.Enqueue("FILE: src/Temp.cs\nCONTENT:\npublic class Temp {}\nEND_FILE");
        _verificationRunner.Result = new VerificationResult(1, "Failed", false);
        _claudeService.Events.Add(new AgentStreamEvent(AgentEventKind.AssistantText, "Claude output"));

        // Act
        var events = await ToListAsync(_service.RunAsync(request));

        // Assert
        Assert.Contains(events, e => e.Text.Contains("Cascade: Reverting cheap-tier edits and escalating to Claude Code..."));
        Assert.Contains(events, e => e.Text == "Claude output");
        Assert.Single(_claudeService.Runs);
        // The cheap tier's untracked file is removed; workspace-wide `git restore .` is never used.
        Assert.False(File.Exists(Path.Combine(_workspace, "src", "Temp.cs")));
        Assert.Empty(_gitService.DiscardedPaths);
    }

    [Fact]
    public async Task Users_Uncommitted_Edit_Survives_Failed_Cheap_Tier()
    {
        // Arrange: the user has an uncommitted edit in a file the cheap tier then overwrites.
        var userFile = Path.Combine(_workspace, "src", "Program.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(userFile)!);
        await File.WriteAllTextAsync(userFile, "// user's uncommitted work");

        var untouchedFile = Path.Combine(_workspace, "notes.txt");
        await File.WriteAllTextAsync(untouchedFile, "user notes");

        var request = new AgentRunRequest("Fix bug", _workspace, VerificationCommand: "dotnet test");
        _geminiService.Responses.Enqueue("[\"src/Program.cs\"]");
        _geminiService.Responses.Enqueue("FILE: src/Program.cs\nCONTENT:\npublic class Broken {}\nEND_FILE");
        _verificationRunner.Result = new VerificationResult(1, "Failed", false);

        // Act
        await ToListAsync(_service.RunAsync(request));

        // Assert: the user's content is restored, unrelated files untouched, no blanket discard.
        Assert.Equal("// user's uncommitted work", await File.ReadAllTextAsync(userFile));
        Assert.Equal("user notes", await File.ReadAllTextAsync(untouchedFile));
        Assert.Empty(_gitService.DiscardedPaths);
        Assert.Single(_claudeService.Runs);
    }

    [Fact]
    public async Task Rejects_Path_Traversal_In_CheapTier_File_Writes()
    {
        // Arrange: the model tries to write outside the workspace.
        var request = new AgentRunRequest("Fix bug", _workspace, VerificationCommand: "dotnet test");
        _geminiService.Responses.Enqueue("[\"src/Temp.cs\"]");
        _geminiService.Responses.Enqueue(
            "FILE: ../evil.txt\nCONTENT:\npwned\nEND_FILE\n" +
            "FILE: src/Safe.cs\nCONTENT:\npublic class Safe {}\nEND_FILE");
        _verificationRunner.Result = new VerificationResult(0, "Passed", false);

        // Act
        var events = await ToListAsync(_service.RunAsync(request));

        // Assert: the traversal path is rejected and logged; the safe write still happens.
        var evilPath = Path.Combine(Path.GetDirectoryName(_workspace)!, "evil.txt");
        Assert.False(File.Exists(evilPath));
        Assert.Contains(events, e => e.Text.Contains("Rejected unsafe file path") && e.Text.Contains("../evil.txt"));
        Assert.True(File.Exists(Path.Combine(_workspace, "src", "Safe.cs")));
    }

    [Fact]
    public async Task Repo_Workspace_Runs_Cheap_Tier_In_Worktree_And_Merges_On_Success()
    {
        // Arrange: workspace is a git repo, so the cheap tier must run in an isolated worktree.
        _gitService.IsRepo = true;
        var request = new AgentRunRequest("Fix bug", _workspace, VerificationCommand: "dotnet test");
        _geminiService.Responses.Enqueue("[\"src/Temp.cs\"]");
        _geminiService.Responses.Enqueue("FILE: src/Temp.cs\nCONTENT:\npublic class Temp {}\nEND_FILE");
        _verificationRunner.Result = new VerificationResult(0, "Passed", false);

        // Act
        var events = await ToListAsync(_service.RunAsync(request));

        // Assert: edits landed in the worktree, not the workspace, and were merged back.
        var worktree = Assert.Single(_gitService.AddedWorktrees);
        Assert.True(File.Exists(Path.Combine(worktree.Path, "src", "Temp.cs")));
        Assert.False(File.Exists(Path.Combine(_workspace, "src", "Temp.cs")));
        Assert.Equal(worktree.Branch, Assert.Single(_gitService.MergedBranches));
        Assert.Single(_gitService.RemovedWorktrees);
        Assert.Contains(events, e => e.Text.Contains("Cascade: Verification passed. Cheap tier implementation succeeded."));
        Assert.Empty(_claudeService.Runs);
        Assert.Empty(_gitService.DiscardedPaths);
    }

    [Fact]
    public async Task Repo_Workspace_Discards_Worktree_Without_Touching_Workspace_On_Failure()
    {
        // Arrange
        _gitService.IsRepo = true;
        var userFile = Path.Combine(_workspace, "src", "Program.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(userFile)!);
        await File.WriteAllTextAsync(userFile, "// user's uncommitted work");

        var request = new AgentRunRequest("Fix bug", _workspace, VerificationCommand: "dotnet test");
        _geminiService.Responses.Enqueue("[\"src/Program.cs\"]");
        _geminiService.Responses.Enqueue("FILE: src/Program.cs\nCONTENT:\npublic class Broken {}\nEND_FILE");
        _verificationRunner.Result = new VerificationResult(1, "Failed", false);
        _claudeService.Events.Add(new AgentStreamEvent(AgentEventKind.AssistantText, "Claude output"));

        // Act
        var events = await ToListAsync(_service.RunAsync(request));

        // Assert: workspace untouched, worktree removed, branch dropped, escalation happened.
        Assert.Equal("// user's uncommitted work", await File.ReadAllTextAsync(userFile));
        var worktree = Assert.Single(_gitService.AddedWorktrees);
        Assert.Equal(worktree.Path, Assert.Single(_gitService.RemovedWorktrees));
        Assert.Empty(_gitService.MergedBranches);
        Assert.Contains(_gitService.DeletedBranches, b => b.Branch == worktree.Branch && b.Force);
        Assert.Contains(events, e => e.Text == "Claude output");
        Assert.Single(_claudeService.Runs);
        Assert.Empty(_gitService.DiscardedPaths);
    }

    private static async Task<List<AgentStreamEvent>> ToListAsync(IAsyncEnumerable<AgentStreamEvent> source)
    {
        var list = new List<AgentStreamEvent>();
        await foreach (var item in source)
        {
            list.Add(item);
        }
        return list;
    }

    private class FakeClaudeService : ITaskOrchestrationService
    {
        public List<AgentRunRequest> Runs { get; } = new();
        public List<AgentStreamEvent> Events { get; set; } = new();

        public async IAsyncEnumerable<AgentStreamEvent> RunAsync(
            AgentRunRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Runs.Add(request);
            await Task.Yield();
            foreach (var evt in Events)
            {
                yield return evt;
            }
        }

        public Task<(int ExitCode, string Output)> RunVerificationAsync(
            string command,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((0, "Verify Passed"));
        }
    }

    private class FakeSearchService : IVectorSearchService
    {
        public List<CodeSearchResult> Hits { get; } = new();
        public string Backend => "fake";
        public Task<int> IndexFileAsync(string r, string f, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> IndexRepositoryAsync(string r, CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<CodeSearchResult>> SearchAsync(
            string q, int topN = 5, string? repo = null, double keywordWeight = 0.5, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<CodeSearchResult>)Hits);
    }

    private class FakeGeminiService : IGeminiService
    {
        public bool IsConfigured => true;
        public List<string> Prompts { get; } = new();
        public Queue<string> Responses { get; } = new();

        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            Prompts.Add(prompt);
            return Task.FromResult(Responses.Count > 0 ? Responses.Dequeue() : string.Empty);
        }

        public Task<string> GenerateResearchPlanAsync(string goal, string repositoryContext, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TaskItem>> GenerateTasksAsync(string plan, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string> TroubleshootAsync(string command, string output, string repositoryContext, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class FakeGitService : IGitService
    {
        public bool IsRepo { get; set; }
        public bool WorktreeAddSucceeds { get; set; } = true;
        public List<string> DiscardedPaths { get; } = new();
        public List<(string Repo, string Path, string Branch)> AddedWorktrees { get; } = new();
        public List<string> MergedBranches { get; } = new();
        public List<string> RemovedWorktrees { get; } = new();
        public List<(string Branch, bool Force)> DeletedBranches { get; } = new();
        public List<string> CommittedPaths { get; } = new();

        public Task<bool> IsRepositoryAsync(string repositoryPath, CancellationToken cancellationToken = default)
            => Task.FromResult(IsRepo);

        public Task<GitStatus> GetStatusAsync(string repositoryPath, CancellationToken cancellationToken = default)
            => Task.FromResult(new GitStatus(IsRepo, "main", Clean: false));

        public Task<GitResult> DiscardChangesAsync(string repositoryPath, CancellationToken cancellationToken = default)
        {
            DiscardedPaths.Add(repositoryPath);
            return Task.FromResult(new GitResult(true, "reverted"));
        }

        public Task<GitResult> AddWorktreeAsync(string repositoryPath, string worktreePath, string branch, CancellationToken cancellationToken = default)
        {
            if (!WorktreeAddSucceeds) return Task.FromResult(new GitResult(false, "worktree add failed"));
            AddedWorktrees.Add((repositoryPath, worktreePath, branch));
            Directory.CreateDirectory(worktreePath);
            return Task.FromResult(new GitResult(true, "worktree added"));
        }

        public Task<GitResult> RemoveWorktreeAsync(string repositoryPath, string worktreePath, CancellationToken cancellationToken = default)
        {
            RemovedWorktrees.Add(worktreePath);
            return Task.FromResult(new GitResult(true, "worktree removed"));
        }

        public Task<GitResult> MergeBranchAsync(string repositoryPath, string branch, CancellationToken cancellationToken = default)
        {
            MergedBranches.Add(branch);
            return Task.FromResult(new GitResult(true, "merged"));
        }

        public Task<GitResult> DeleteBranchAsync(string repositoryPath, string branch, bool force = false, CancellationToken cancellationToken = default)
        {
            DeletedBranches.Add((branch, force));
            return Task.FromResult(new GitResult(true, "deleted"));
        }

        public Task<GitResult> CommitAllAsync(string repositoryPath, string message, CancellationToken cancellationToken = default)
        {
            CommittedPaths.Add(repositoryPath);
            return Task.FromResult(new GitResult(true, "committed"));
        }

        public Task<GitResult> CreateBranchAsync(string repositoryPath, string branch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<string>> GetRecentCommits(string repositoryPath, int count, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class FakeVerificationRunner : IVerificationRunner
    {
        public List<string> Commands { get; } = new();
        public VerificationResult Result { get; set; } = new(0, "Passed", false);

        public Task<VerificationResult> RunAsync(string command, string workingDirectory, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(Result);
        }
    }
}
