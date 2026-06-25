using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Core.Search;
using Xunit;

namespace WheelHouse.Tests;

public class RagContextTests
{
    private static CodeSearchResult Hit(string file, string snippet) =>
        new(new CodeIndexEntry { FilePath = file, Snippet = snippet }, 0.9);

    [Fact]
    public void Empty_Results_Yields_Path_Only_Context()
    {
        var ctx = RagContext.Build("/repo", Array.Empty<CodeSearchResult>());
        Assert.Equal("Repository: /repo", ctx);
    }

    [Fact]
    public void Includes_Relative_Paths_And_Fenced_Snippets()
    {
        var results = new[]
        {
            Hit(Path.Combine("/repo", "src", "Auth.cs"), "class Auth {}"),
            Hit(Path.Combine("/repo", "src", "Login.cs"), "class Login {}")
        };

        var ctx = RagContext.Build("/repo", results);

        Assert.Contains("Repository: /repo", ctx);
        Assert.Contains("class Auth {}", ctx);
        Assert.Contains("class Login {}", ctx);
        Assert.Contains("```", ctx);
        // Paths are relative to the repo root, not absolute.
        Assert.DoesNotContain(Path.Combine("/repo", "src", "Auth.cs"), ctx);
        Assert.Contains("Auth.cs", ctx);
    }

    [Fact]
    public void Truncates_Oversized_Snippets()
    {
        var big = new string('x', 5000);
        var ctx = RagContext.Build("/repo", new[] { Hit("/repo/Big.cs", big) }, maxSnippetChars: 100);
        Assert.Contains("…", ctx);
        Assert.True(ctx.Length < 1000);
    }
}
