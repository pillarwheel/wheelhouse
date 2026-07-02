using WheelHouse.Core.Agents;
using WheelHouse.Infrastructure.Agents;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>Proves the CLI's final result line yields real token/cost/duration accounting.</summary>
public class AgentUsageParsingTests
{
    [Fact]
    public void Result_Line_With_Usage_Yields_Tokens_Cost_And_Duration()
    {
        var evt = ClaudeCliService.ParseLine(
            """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":5210,"result":"done",
             "total_cost_usd":0.0731,
             "usage":{"input_tokens":42,"cache_creation_input_tokens":1000,"cache_read_input_tokens":8000,"output_tokens":650}}
            """.Replace("\n", ""));

        Assert.NotNull(evt);
        Assert.Equal(AgentEventKind.Result, evt!.Kind);
        var usage = evt.Usage!;
        Assert.Equal(42 + 1000 + 8000, usage.InputTokens); // cache reads/writes count as consumption
        Assert.Equal(650, usage.OutputTokens);
        Assert.Equal(9692, usage.TotalTokens);
        Assert.Equal(5210, usage.DurationMs);
        Assert.Equal(0.0731, usage.CostUsd!.Value, precision: 6);
    }

    [Fact]
    public void Result_Line_Without_Cost_Still_Reports_Tokens()
    {
        // Subscription-auth runs report usage but no total_cost_usd.
        var evt = ClaudeCliService.ParseLine(
            """{"type":"result","is_error":false,"duration_ms":900,"result":"ok","usage":{"input_tokens":10,"output_tokens":5}}""");

        var usage = evt!.Usage!;
        Assert.Equal(15, usage.TotalTokens);
        Assert.Null(usage.CostUsd);
    }

    [Fact]
    public void Result_Line_Without_Usage_Or_Timing_Has_Null_Usage()
    {
        var evt = ClaudeCliService.ParseLine("""{"type":"result","is_error":false,"result":"ok"}""");
        Assert.Null(evt!.Usage);
    }

    [Fact]
    public void Non_Result_Lines_Never_Carry_Usage()
    {
        var evt = ClaudeCliService.ParseLine(
            """{"type":"assistant","message":{"content":[{"type":"text","text":"hi"}]}}""");
        Assert.NotNull(evt);
        Assert.Null(evt!.Usage);
    }
}
