using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Core.Search;
using Xunit;

namespace WheelHouse.Tests;

public class SearchFusionTests
{
    private static CodeSearchResult R(int id, string file, double score = 0.5)
        => new(new CodeIndexEntry { Id = id, FilePath = file, SymbolName = file }, score);

    [Fact]
    public void Hit_In_Both_Legs_Outranks_Hits_In_One()
    {
        var semantic = new[] { R(1, "only-semantic.cs"), R(2, "both.cs") };
        var keyword = new[] { R(3, "only-keyword.cs"), R(2, "both.cs") };

        var fused = SearchFusion.ReciprocalRankFusion(semantic, keyword, topN: 3);

        Assert.Equal("both.cs", fused[0].Entry.FilePath);
        Assert.Equal(3, fused.Count);
    }

    [Fact]
    public void Respects_TopN()
    {
        var semantic = Enumerable.Range(1, 10).Select(i => R(i, $"s{i}.cs")).ToArray();
        var keyword = Enumerable.Range(11, 10).Select(i => R(i, $"k{i}.cs")).ToArray();

        var fused = SearchFusion.ReciprocalRankFusion(semantic, keyword, topN: 4);

        Assert.Equal(4, fused.Count);
    }

    [Fact]
    public void Earlier_Ranks_Contribute_More()
    {
        // Item 5 is first in keyword; item 1 is first in semantic. Both single-leg:
        // fusion preserves each leg's internal order among single-leg hits.
        var semantic = new[] { R(1, "a.cs"), R(2, "b.cs") };
        var keyword = new[] { R(5, "e.cs"), R(6, "f.cs") };

        var fused = SearchFusion.ReciprocalRankFusion(semantic, keyword, topN: 4);

        var files = fused.Select(f => f.Entry.FilePath).ToList();
        Assert.True(files.IndexOf("a.cs") < files.IndexOf("b.cs"));
        Assert.True(files.IndexOf("e.cs") < files.IndexOf("f.cs"));
    }

    [Fact]
    public void Unsaved_Entries_Fuse_By_Path_And_Symbol()
    {
        // Id = 0 (not persisted) → identity falls back to FilePath|SymbolName.
        var semantic = new[] { R(0, "x.cs") };
        var keyword = new[] { R(0, "x.cs"), R(0, "y.cs") };

        var fused = SearchFusion.ReciprocalRankFusion(semantic, keyword, topN: 5);

        Assert.Equal(2, fused.Count);
        Assert.Equal("x.cs", fused[0].Entry.FilePath); // present in both legs
    }

    [Fact]
    public void Empty_Legs_Are_Harmless()
    {
        var some = new[] { R(1, "a.cs") };
        Assert.Single(SearchFusion.ReciprocalRankFusion(some, Array.Empty<CodeSearchResult>(), 5));
        Assert.Single(SearchFusion.ReciprocalRankFusion(Array.Empty<CodeSearchResult>(), some, 5));
        Assert.Empty(SearchFusion.ReciprocalRankFusion(
            Array.Empty<CodeSearchResult>(), Array.Empty<CodeSearchResult>(), 5));
    }
}
