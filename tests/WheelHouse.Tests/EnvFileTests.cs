using WheelHouse.Infrastructure.Configuration;
using Xunit;

namespace WheelHouse.Tests;

public class EnvFileTests
{
    [Fact]
    public void Loads_Keys_Skips_Comments_And_Strips_Quotes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wh_env_{Guid.NewGuid():N}.env");
        var k1 = "WH_TEST_" + Guid.NewGuid().ToString("N");
        var k2 = "WH_TEST_" + Guid.NewGuid().ToString("N");
        var k3 = "WH_TEST_" + Guid.NewGuid().ToString("N");
        File.WriteAllText(path,
            $"""
            # a comment
            {k1}=plain-value

            {k2}="quoted value"
            export {k3}='single'
            NOT_AN_ASSIGNMENT
            """);

        try
        {
            var applied = EnvFile.Load(path);
            Assert.Equal(3, applied);
            Assert.Equal("plain-value", Environment.GetEnvironmentVariable(k1));
            Assert.Equal("quoted value", Environment.GetEnvironmentVariable(k2));
            Assert.Equal("single", Environment.GetEnvironmentVariable(k3));
        }
        finally
        {
            foreach (var k in new[] { k1, k2, k3 }) Environment.SetEnvironmentVariable(k, null);
            File.Delete(path);
        }
    }

    [Fact]
    public void Does_Not_Override_Existing_Real_Environment()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wh_env_{Guid.NewGuid():N}.env");
        var key = "WH_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(key, "real");
        File.WriteAllText(path, $"{key}=from-file");

        try
        {
            var applied = EnvFile.Load(path);
            Assert.Equal(0, applied);
            Assert.Equal("real", Environment.GetEnvironmentVariable(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
            File.Delete(path);
        }
    }
}
