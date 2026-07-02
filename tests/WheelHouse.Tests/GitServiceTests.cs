using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// Integration tests against a real temporary git repository. They skip automatically when the
/// <c>git</c> CLI isn't available (so they're safe everywhere, and run on CI where git is present).
/// </summary>
public class GitServiceTests
{
    private static bool RunGit(string dir, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit(15000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string NewRepo(out bool gitAvailable)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"wh_git_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        gitAvailable = RunGit(dir, "init");
        if (gitAvailable)
        {
            RunGit(dir, "config", "user.email", "t@example.com");
            RunGit(dir, "config", "user.name", "Test");
            File.WriteAllText(Path.Combine(dir, "a.txt"), "one");
            RunGit(dir, "add", ".");
            RunGit(dir, "commit", "-m", "init");
        }
        return dir;
    }

    [Fact]
    public async Task Status_Discard_And_Branch_RoundTrip()
    {
        var dir = NewRepo(out var gitAvailable);
        if (!gitAvailable) return; // git not installed — skip
        try
        {
            var git = new GitService(NullLogger<GitService>.Instance);

            Assert.True(await git.IsRepositoryAsync(dir));

            var clean = await git.GetStatusAsync(dir);
            Assert.True(clean.IsRepo);
            Assert.True(clean.Clean);

            // Simulate an agent edit.
            File.WriteAllText(Path.Combine(dir, "a.txt"), "two");
            var dirty = await git.GetStatusAsync(dir);
            Assert.False(dirty.Clean);

            // Discard rolls the tracked file back to HEAD.
            var discard = await git.DiscardChangesAsync(dir);
            Assert.True(discard.Success);
            Assert.Equal("one", File.ReadAllText(Path.Combine(dir, "a.txt")));
            Assert.True((await git.GetStatusAsync(dir)).Clean);

            // Session branch isolation.
            var branch = await git.CreateBranchAsync(dir, "wheelhouse/session-1");
            Assert.True(branch.Success);
            Assert.Equal("wheelhouse/session-1", (await git.GetStatusAsync(dir)).Branch);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Worktree_RoundTrip_Isolates_Commits_And_Merges_Back()
    {
        var dir = NewRepo(out var gitAvailable);
        if (!gitAvailable) return; // git not installed — skip
        var wt = Path.Combine(Path.GetTempPath(), $"wh_wt_{Guid.NewGuid():N}");
        try
        {
            var git = new GitService(NullLogger<GitService>.Instance);

            var add = await git.AddWorktreeAsync(dir, wt, "wheelhouse/parallel-test");
            Assert.True(add.Success, add.Output);
            Assert.True(Directory.Exists(wt));

            // Work in the worktree is invisible to the main tree until merged.
            File.WriteAllText(Path.Combine(wt, "branch.txt"), "from-branch");
            var commit = await git.CommitAllAsync(wt, "branch work");
            Assert.True(commit.Success, commit.Output);
            Assert.False(File.Exists(Path.Combine(dir, "branch.txt")));

            var merge = await git.MergeBranchAsync(dir, "wheelhouse/parallel-test");
            Assert.True(merge.Success, merge.Output);
            Assert.Equal("from-branch", File.ReadAllText(Path.Combine(dir, "branch.txt")));

            Assert.True((await git.RemoveWorktreeAsync(dir, wt)).Success);
            Assert.False(Directory.Exists(wt));
            Assert.True((await git.DeleteBranchAsync(dir, "wheelhouse/parallel-test")).Success);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
            try { Directory.Delete(wt, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Conflicting_Merge_Is_Aborted_And_Leaves_Tree_Untouched()
    {
        var dir = NewRepo(out var gitAvailable);
        if (!gitAvailable) return;
        var wt = Path.Combine(Path.GetTempPath(), $"wh_wt_{Guid.NewGuid():N}");
        try
        {
            var git = new GitService(NullLogger<GitService>.Instance);
            Assert.True((await git.AddWorktreeAsync(dir, wt, "wheelhouse/conflict-test")).Success);

            // Both sides edit a.txt → guaranteed conflict.
            File.WriteAllText(Path.Combine(wt, "a.txt"), "branch-version");
            await git.CommitAllAsync(wt, "branch edit");
            File.WriteAllText(Path.Combine(dir, "a.txt"), "main-version");
            await git.CommitAllAsync(dir, "main edit");

            var merge = await git.MergeBranchAsync(dir, "wheelhouse/conflict-test");

            Assert.False(merge.Success);
            // Abort restored the pre-merge state: no conflict markers on disk.
            Assert.Equal("main-version", File.ReadAllText(Path.Combine(dir, "a.txt")));
            Assert.True((await git.GetStatusAsync(dir)).Clean);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
            try { Directory.Delete(wt, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Non_Repo_Reports_Not_A_Repo()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"wh_nogit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var status = await new GitService(NullLogger<GitService>.Instance).GetStatusAsync(dir);
            Assert.False(status.IsRepo);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
