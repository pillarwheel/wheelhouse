using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class VerificationRunnerTests
{
    private static PowerShellVerificationRunner New() => new(NullLogger<PowerShellVerificationRunner>.Instance);

    [Fact]
    public async Task Successful_Command_Reports_Exit_Zero_And_Output()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await New().RunAsync("Write-Output 'verify-ok'; exit 0", Directory.GetCurrentDirectory());
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("verify-ok", result.Output);
    }

    [Fact]
    public async Task Failing_Command_Reports_NonZero_Exit()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await New().RunAsync("Write-Output 'boom'; exit 3", Directory.GetCurrentDirectory());
        Assert.False(result.Succeeded);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("boom", result.Output);
    }

    [Fact]
    public async Task Empty_Command_Is_NoOp_Success()
    {
        var result = await New().RunAsync("   ", Directory.GetCurrentDirectory());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Times_Out_Long_Commands()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await New().RunAsync("Start-Sleep -Seconds 30", Directory.GetCurrentDirectory(),
            timeout: TimeSpan.FromMilliseconds(800));
        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
    }
}
