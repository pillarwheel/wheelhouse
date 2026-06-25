using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class VectorMathTests
{
    [Fact]
    public void Identical_Vectors_Score_One()
    {
        var a = new[] { 1f, 2f, 3f };
        Assert.Equal(1.0, VectorMath.CosineSimilarity(a, a), 5);
    }

    [Fact]
    public void Orthogonal_Vectors_Score_Zero()
    {
        var a = new[] { 1f, 0f };
        var b = new[] { 0f, 1f };
        Assert.Equal(0.0, VectorMath.CosineSimilarity(a, b), 5);
    }

    [Fact]
    public void Mismatched_Or_Empty_Returns_Zero()
    {
        Assert.Equal(0.0, VectorMath.CosineSimilarity(new[] { 1f }, new[] { 1f, 2f }));
        Assert.Equal(0.0, VectorMath.CosineSimilarity(Array.Empty<float>(), Array.Empty<float>()));
    }
}
