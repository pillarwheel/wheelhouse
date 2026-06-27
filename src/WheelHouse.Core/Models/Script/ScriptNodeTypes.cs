namespace WheelHouse.Core.Models.Script;

/// <summary>Static metadata about a node type: its label, ports, and editable settings.</summary>
/// <param name="Type">The <see cref="ScriptNode.Type"/> discriminator.</param>
/// <param name="Label">Friendly name shown in the toolbox/editor.</param>
/// <param name="Inputs">Input port names (left side). The control-entry port is conventionally <c>Execute</c>.</param>
/// <param name="Outputs">Output port names (right side).</param>
/// <param name="Settings">Editable setting keys exposed in the sidebar editor.</param>
public record ScriptNodeType(
    string Type,
    string Label,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<ScriptSettingField> Settings);

/// <summary>An editable setting on a node, rendered in the editor sidebar.</summary>
/// <param name="Key">Key into <see cref="ScriptNode.Settings"/>.</param>
/// <param name="Label">Field label.</param>
/// <param name="Multiline">Whether to render a textarea (true) or a single-line input (false).</param>
/// <param name="Options">When non-null, render a dropdown with these choices instead of a text input.</param>
public record ScriptSettingField(
    string Key,
    string Label,
    bool Multiline = false,
    IReadOnlyList<string>? Options = null);

/// <summary>The catalog of supported node types and their port/setting layouts.</summary>
public static class ScriptNodeTypes
{
    public const string ControlPort = "Execute";

    // Shared setting exposed on LLM/agent nodes: scopes the workspace files fed as context.
    private static readonly ScriptSettingField ContextGlobs =
        new("ContextGlobs", "Context files (glob, comma-sep)");

    public static readonly IReadOnlyList<ScriptNodeType> All = new[]
    {
        new ScriptNodeType("start", "Start",
            Inputs: Array.Empty<string>(),
            Outputs: new[] { "Next", "Goal", "RepositoryPath" },
            Settings: Array.Empty<ScriptSettingField>()),

        new ScriptNodeType("gemini-prompt", "Gemini Research",
            Inputs: new[] { "Execute", "Goal", "Context" },
            Outputs: new[] { "Next", "Response" },
            Settings: new[] { new ScriptSettingField("Prompt", "Prompt template", true), ContextGlobs }),

        new ScriptNodeType("claude-prompt", "Claude Research",
            Inputs: new[] { "Execute", "Goal", "Context" },
            Outputs: new[] { "Success", "Failure", "Response" },
            Settings: new[] { new ScriptSettingField("Prompt", "Prompt template", true), ContextGlobs }),

        new ScriptNodeType("claude-execute", "Claude Execute",
            Inputs: new[] { "Execute", "Goal", "Context" },
            Outputs: new[] { "Success", "Failure", "Response" },
            Settings: new[]
            {
                new ScriptSettingField("Prompt", "Implementation prompt", true),
                ContextGlobs,
                // Atomic git commits (research_notes §2): commit the stage's changes on success.
                new ScriptSettingField("AutoCommit", "Auto-commit on success", Options: new[] { "off", "on" })
            }),

        new ScriptNodeType("dialogue", "Dialogue",
            Inputs: new[] { "Execute", "Goal", "Context" },
            Outputs: new[] { "Next", "Response" },
            Settings: new[]
            {
                new ScriptSettingField("Prompt", "Topic / seed proposal", true),
                new ScriptSettingField("Rounds", "Rounds"),
                ContextGlobs
            }),

        new ScriptNodeType("verification", "Shell Verify",
            Inputs: new[] { "Execute" },
            Outputs: new[] { "Passed", "Failed", "Response" },
            Settings: new[] { new ScriptSettingField("Command", "Command line") }),

        new ScriptNodeType("git-command", "Git Command",
            Inputs: new[] { "Execute" },
            Outputs: new[] { "Success", "Failure", "Response" },
            Settings: new[] { new ScriptSettingField("Command", "Git args (e.g. commit -am \"msg\")") }),

        new ScriptNodeType("loop-count", "Loop",
            Inputs: new[] { "Execute", "Reset" },
            Outputs: new[] { "Loop", "Done", "Count" },
            Settings: new[] { new ScriptSettingField("MaxIterations", "Max iterations") }),

        new ScriptNodeType("conditional", "Conditional",
            Inputs: new[] { "Execute", "ValueA", "ValueB" },
            Outputs: new[] { "True", "False" },
            Settings: new[]
            {
                new ScriptSettingField("Operator", "Operator",
                    Options: new[] { "Equals", "Contains", "IsEmpty" }),
                new ScriptSettingField("CompareTo", "Compare to (literal, if ValueB unwired)")
            }),

        new ScriptNodeType("parallel", "Parallel",
            Inputs: new[] { "Execute" },
            Outputs: new[] { "BranchA", "BranchB" },
            Settings: Array.Empty<ScriptSettingField>()),

        new ScriptNodeType("merge", "Merge",
            Inputs: new[] { "A", "B" },
            Outputs: new[] { "Next" },
            Settings: Array.Empty<ScriptSettingField>()),

        new ScriptNodeType("wait-approval", "Wait Approval",
            Inputs: new[] { "Execute" },
            Outputs: new[] { "Approved", "Rejected" },
            Settings: new[] { new ScriptSettingField("Message", "Approval prompt", true) }),

        new ScriptNodeType("generate-tasks", "Generate Tasks",
            Inputs: new[] { "Execute", "Plan" },
            Outputs: new[] { "Next" },
            Settings: Array.Empty<ScriptSettingField>()),
    };

    public static ScriptNodeType? Find(string type)
        => All.FirstOrDefault(t => string.Equals(t.Type, type, StringComparison.OrdinalIgnoreCase));

    /// <summary>Output ports that carry control flow (vs. data ports like Response/Goal/Count).</summary>
    public static readonly IReadOnlySet<string> ControlOutputPorts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Next", "Success", "Failure", "Passed", "Failed", "Loop", "Done",
            "True", "False", "Approved", "Rejected", "BranchA", "BranchB"
        };

    /// <summary>Control-flow input ports that trigger a node (vs. data inputs like Goal/Context).</summary>
    public static readonly IReadOnlySet<string> ControlInputPorts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ControlPort, "Reset", "A", "B" };

    /// <summary>True when a port carries control flow (either direction), used for editor/view coloring.</summary>
    public static bool IsControlPort(string port)
        => ControlOutputPorts.Contains(port) || ControlInputPorts.Contains(port);
}
