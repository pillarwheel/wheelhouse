using WheelHouse.Core.Models.Script;
using Xunit;

namespace WheelHouse.Tests;

public class ScriptGraphTests
{
    [Fact]
    public void RoundTrips_Through_Json()
    {
        var graph = new ScriptGraph
        {
            Nodes =
            {
                new ScriptNode { Id = "n1", Type = "start", Name = "Start", X = 10, Y = 20 },
                new ScriptNode
                {
                    Id = "n2", Type = "gemini-prompt", Name = "Research", X = 300, Y = 40,
                    Settings = { ["Prompt"] = "Research {{GOAL}}" }
                }
            },
            Edges =
            {
                new ScriptEdge { Id = "e1", SourceNodeId = "n1", SourcePort = "Next", TargetNodeId = "n2", TargetPort = "Execute" },
                new ScriptEdge { Id = "e2", SourceNodeId = "n1", SourcePort = "Goal", TargetNodeId = "n2", TargetPort = "Goal" }
            }
        };

        var json = graph.ToJson();
        var restored = ScriptGraph.FromJson(json);

        Assert.Equal(2, restored.Nodes.Count);
        Assert.Equal(2, restored.Edges.Count);

        var research = Assert.Single(restored.Nodes, n => n.Id == "n2");
        Assert.Equal("gemini-prompt", research.Type);
        Assert.Equal(300, research.X);
        Assert.Equal("Research {{GOAL}}", research.Settings["Prompt"]);

        Assert.Equal("n1", restored.StartNode?.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not valid json")]
    public void FromJson_Returns_Empty_Graph_For_Invalid_Input(string? json)
    {
        var graph = ScriptGraph.FromJson(json);
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.Null(graph.StartNode);
    }

    [Fact]
    public void NodeType_Catalog_Exposes_Expected_Ports()
    {
        var gemini = ScriptNodeTypes.Find("gemini-prompt");
        Assert.NotNull(gemini);
        Assert.Contains("Execute", gemini!.Inputs);
        Assert.Contains("Response", gemini.Outputs);

        var verify = ScriptNodeTypes.Find("verification");
        Assert.NotNull(verify);
        Assert.Contains("Passed", verify!.Outputs);
        Assert.Contains("Failed", verify.Outputs);

        var approval = ScriptNodeTypes.Find("wait-approval");
        Assert.NotNull(approval);
        Assert.Contains("Approved", approval!.Outputs);
        Assert.Contains("Rejected", approval.Outputs);

        // Palette refinements: Claude branches on Success/Failure; Loop exposes Reset/Count.
        var claude = ScriptNodeTypes.Find("claude-execute");
        Assert.Contains("Success", claude!.Outputs);
        Assert.Contains("Failure", claude.Outputs);
        Assert.Contains(claude.Settings, s => s.Key == "AutoCommit" && s.Options is not null);

        var loop = ScriptNodeTypes.Find("loop-count");
        Assert.Contains("Reset", loop!.Inputs);
        Assert.Contains("Count", loop.Outputs);

        var cond = ScriptNodeTypes.Find("conditional");
        Assert.Contains("ValueA", cond!.Inputs);
        Assert.Contains("ValueB", cond.Inputs);
        Assert.Contains(cond.Settings, s => s.Key == "Operator" && s.Options is not null);

        // Parallel pipeline (pattern B): fork/join nodes.
        var parallel = ScriptNodeTypes.Find("parallel");
        Assert.Contains("BranchA", parallel!.Outputs);
        Assert.Contains("BranchB", parallel.Outputs);
        var merge = ScriptNodeTypes.Find("merge");
        Assert.Contains("A", merge!.Inputs);
        Assert.Contains("B", merge.Inputs);

        // Control-port classification: control I/O vs data ports.
        Assert.Contains("Passed", ScriptNodeTypes.ControlOutputPorts);
        Assert.True(ScriptNodeTypes.IsControlPort("BranchA"));
        Assert.True(ScriptNodeTypes.IsControlPort("A"));
        Assert.Contains("Approved", ScriptNodeTypes.ControlOutputPorts);
        Assert.DoesNotContain("Response", ScriptNodeTypes.ControlOutputPorts);
        Assert.True(ScriptNodeTypes.IsControlPort("Reset"));   // control input
        Assert.True(ScriptNodeTypes.IsControlPort("Approved")); // control output
        Assert.False(ScriptNodeTypes.IsControlPort("Count"));   // data output
        Assert.False(ScriptNodeTypes.IsControlPort("Goal"));    // data input
    }
}
