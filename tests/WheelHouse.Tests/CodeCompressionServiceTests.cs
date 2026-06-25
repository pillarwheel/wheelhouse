using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class CodeCompressionServiceTests
{
    private readonly CodeCompressionService _sut = new();

    [Fact]
    public void Removes_Line_Comments()
    {
        var input = "var x = 1; // set x\nvar y = 2;";
        var result = _sut.Compress(input);
        Assert.DoesNotContain("set x", result);
        Assert.Contains("var x = 1;", result);
        Assert.Contains("var y = 2;", result);
    }

    [Fact]
    public void Removes_Block_And_Doc_Comments()
    {
        var input = "/// <summary>doc</summary>\n/* block */ int a = 5;\nint b = 6;";
        var result = _sut.Compress(input);
        Assert.DoesNotContain("summary", result);
        Assert.DoesNotContain("block", result);
        Assert.Contains("int a = 5;", result);
    }

    [Fact]
    public void Preserves_Comment_Like_Text_Inside_Strings()
    {
        var input = "var url = \"http://example.com\"; // trailing";
        var result = _sut.Compress(input);
        Assert.Contains("http://example.com", result);
        Assert.DoesNotContain("trailing", result);
    }

    [Fact]
    public void Drops_Blank_Lines()
    {
        var input = "a;\n\n\n\nb;";
        var result = _sut.Compress(input);
        Assert.Equal("a;\nb;", result);
    }
}
