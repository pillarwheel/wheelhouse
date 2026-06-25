namespace WheelHouse.Core.Models;

/// <summary>
/// Represents a repository under WheelHouse management. Configuration is mirrored
/// to a <c>.wheelhouse/config.yaml</c> file inside the target repository (GitOps style),
/// so the same settings travel with the repo across machines.
/// </summary>
public class Workspace
{
    public int Id { get; set; }

    /// <summary>Friendly display name shown in the dashboard.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute path to the repository root on disk.</summary>
    public string AbsolutePath { get; set; } = string.Empty;

    /// <summary>Default git branch used when starting sessions.</summary>
    public string DefaultBranch { get; set; } = "main";

    /// <summary>
    /// Claude permission mode used when running tasks in this workspace:
    /// <c>acceptEdits</c> (default — auto-accept file edits), <c>default</c>, or <c>bypassPermissions</c>.
    /// </summary>
    public string PermissionMode { get; set; } = "acceptEdits";

    /// <summary>JSON blob of environment variables injected into agent subprocesses.</summary>
    public string EnvironmentJson { get; set; } = "{}";

    /// <summary>Whether this workspace is currently the active one in the UI.</summary>
    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>Local RAG index state (auto-indexed in the background when added).</summary>
    public IndexState IndexStatus { get; set; } = IndexState.None;

    /// <summary>Number of files in the local code index for this workspace.</summary>
    public int IndexedFileCount { get; set; }

    public DateTime? LastIndexedAt { get; set; }

    public List<CommandRule> CommandRules { get; set; } = new();
    public List<AgentSession> Sessions { get; set; } = new();
}
