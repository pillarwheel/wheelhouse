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
