using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class BenchmarkServiceTests
{
    [Fact]
    public void GetBuiltInChallenges_Returns_All_Default_Challenges()
    {
        // Arrange
        var service = new BenchmarkService();

        // Act
        var challenges = service.GetBuiltInChallenges();

        // Assert
        Assert.Equal(5, challenges.Count);
        Assert.Contains(challenges, c => c.Id == "FIB");
        Assert.Contains(challenges, c => c.Id == "YML");
        Assert.Contains(challenges, c => c.Id == "COR");
        Assert.Contains(challenges, c => c.Id == "BTR");
        Assert.Contains(challenges, c => c.Id == "JSN");
    }

    [Fact]
    public async Task RunBenchmark_Calculates_Cascade_Report_Accurately()
    {
        // Arrange
        var service = new BenchmarkService();

        // Act
        var report = await service.RunBenchmarkAsync("Cascade", simulate: true);

        // Assert
        Assert.Equal("Cascade", report.ConfigName);
        Assert.Equal(5, report.Total);
        Assert.True(report.Solved >= 4); // Cascade resolves simple + fallback complex
        Assert.True(report.SolveRate >= 0.8);
        Assert.True(report.AvgDurationMs > 0.0);
        Assert.True(report.TotalCost > 0.0);
        Assert.Equal(5, report.Results.Count);
    }

    [Fact]
    public async Task RunBenchmark_GeminiOnly_Has_Lower_SolveRate_Than_Cascade()
    {
        // Arrange
        var service = new BenchmarkService();

        // Act
        var geminiReport = await service.RunBenchmarkAsync("GeminiOnly", simulate: true);
        var cascadeReport = await service.RunBenchmarkAsync("Cascade", simulate: true);

        // Assert: Gemini only resolves FIB and YML (2 out of 5), while Cascade resolves more.
        Assert.Equal(2, geminiReport.Solved);
        Assert.Equal(0.4, geminiReport.SolveRate);
        Assert.True(cascadeReport.Solved > geminiReport.Solved);
    }
}
