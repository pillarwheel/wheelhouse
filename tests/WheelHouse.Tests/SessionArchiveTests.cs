using WheelHouse.Core.Export;
using Xunit;

namespace WheelHouse.Tests;

public class SessionArchiveTests
{
    [Fact]
    public void RelativePath_Uses_Wheelhouse_Sessions_Folder()
    {
        var rel = SessionArchive.RelativePath(12);
        Assert.Equal(Path.Combine(".wheelhouse", "sessions", "12.md"), rel);
    }

    [Fact]
    public void FullPath_Is_Under_Repository()
    {
        var full = SessionArchive.FullPath(Path.Combine("C:", "repo"), 7);
        Assert.Equal(Path.Combine("C:", "repo", ".wheelhouse", "sessions", "7.md"), full);
    }
}
