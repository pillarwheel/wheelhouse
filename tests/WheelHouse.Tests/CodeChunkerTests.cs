using WheelHouse.Core.Search;
using Xunit;

namespace WheelHouse.Tests;

public class CodeChunkerTests
{
    [Fact]
    public void Small_Text_Is_A_Single_Chunk()
    {
        var chunks = CodeChunker.Split("public class A { }");
        Assert.Equal("public class A { }", Assert.Single(chunks));
    }

    [Fact]
    public void Empty_Text_Yields_No_Chunks()
    {
        Assert.Empty(CodeChunker.Split(""));
        Assert.Empty(CodeChunker.Split("   \n  "));
    }

    [Fact]
    public void Large_Text_Splits_On_Line_Boundaries_Without_Losing_Content()
    {
        var lines = Enumerable.Range(1, 200).Select(i => $"var line{i} = {i};").ToArray();
        var text = string.Join('\n', lines);

        var chunks = CodeChunker.Split(text, targetSize: 500, overlapSize: 100);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 500, $"chunk of {c.Length} chars exceeds target"));
        // Every original line survives in at least one chunk (no truncation loss).
        foreach (var line in lines)
            Assert.Contains(chunks, c => c.Contains(line));
    }

    [Fact]
    public void Consecutive_Chunks_Share_Overlap_Context()
    {
        var lines = Enumerable.Range(1, 100).Select(i => $"line-{i:D3}").ToArray();
        var chunks = CodeChunker.Split(string.Join('\n', lines), targetSize: 300, overlapSize: 60);

        for (var i = 1; i < chunks.Count; i++)
        {
            var lastLineOfPrev = chunks[i - 1].Split('\n')[^1];
            Assert.Contains(lastLineOfPrev, chunks[i]);
        }
    }

    [Fact]
    public void Pathological_Single_Line_Is_Hard_Split()
    {
        var text = new string('x', 5000); // e.g. a minified bundle
        var chunks = CodeChunker.Split(text, targetSize: 1000, overlapSize: 100);

        Assert.True(chunks.Count >= 5);
        Assert.Equal(5000, chunks.Sum(c => c.Replace("\n", "").Length));
    }

    [Fact]
    public void Oversized_Overlap_Is_Clamped_And_Still_Terminates()
    {
        var text = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"row {i}"));
        var chunks = CodeChunker.Split(text, targetSize: 100, overlapSize: 100_000);
        Assert.True(chunks.Count > 1);
    }
}
