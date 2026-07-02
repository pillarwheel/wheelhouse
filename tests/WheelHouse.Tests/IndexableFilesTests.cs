using WheelHouse.Core.Search;
using Xunit;

namespace WheelHouse.Tests;

public class IndexableFilesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "repo");

    [Theory]
    [InlineData("src/Program.cs", true)]
    [InlineData("README.md", true)]
    [InlineData("app/pages/Index.razor", true)]
    [InlineData("scripts/build.yml", true)]
    [InlineData("image.png", false)]
    [InlineData("binary.dll", false)]
    [InlineData("notes.txt", false)]
    public void Filters_By_Extension(string relative, bool expected)
    {
        var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(expected, IndexableFiles.IsIndexable(Root, full));
    }

    [Theory]
    [InlineData("obj/Debug/Generated.cs")]
    [InlineData("src/App/bin/Release/Thing.cs")]
    [InlineData("node_modules/pkg/index.js")]
    [InlineData(".git/hooks/sample.md")]
    [InlineData("OBJ/Upper.cs")] // case-insensitive
    public void Rejects_Files_Under_Ignored_Directories(string relative)
    {
        var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.False(IndexableFiles.IsIndexable(Root, full));
    }

    [Fact]
    public void Rejects_Paths_Outside_The_Root()
    {
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "file.cs");
        Assert.False(IndexableFiles.IsIndexable(Root, outside));
    }

    [Fact]
    public void A_File_Named_Like_An_Ignored_Dir_Is_Still_Indexed()
    {
        // Only directory segments are checked — "obj.cs" the file is fine.
        var full = Path.Combine(Root, "src", "obj.cs");
        Assert.True(IndexableFiles.IsIndexable(Root, full));
    }
}
