using WheelHouse.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WheelHouse.Infrastructure.Services;

/// <summary>Serializable shape of <c>.wheelhouse/config.yaml</c>.</summary>
public class WorkspaceConfigFile
{
    public string Name { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public List<AutoApproveRuleEntry> AutoApprove { get; set; } = new();

    public class AutoApproveRuleEntry
    {
        public string Pattern { get; set; } = string.Empty;
        public string Match { get; set; } = "Prefix";
        public string Action { get; set; } = "AutoApprove";
    }
}

/// <summary>
/// Mirrors workspace configuration to/from a <c>.wheelhouse/config.yaml</c> file inside the
/// target repository (GitOps), so settings version-control alongside the code.
/// </summary>
public class WorkspaceConfigService
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static string ConfigPathFor(string repositoryRoot)
        => Path.Combine(repositoryRoot, ".wheelhouse", "config.yaml");

    /// <summary>Writes the workspace (and its rules) out to the repo's config file.</summary>
    public async Task WriteAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        var path = ConfigPathFor(workspace.AbsolutePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var file = new WorkspaceConfigFile
        {
            Name = workspace.Name,
            DefaultBranch = workspace.DefaultBranch,
            AutoApprove = workspace.CommandRules.Select(r => new WorkspaceConfigFile.AutoApproveRuleEntry
            {
                Pattern = r.Pattern,
                Match = r.MatchType.ToString(),
                Action = r.Action.ToString()
            }).ToList()
        };

        await File.WriteAllTextAsync(path, Serializer.Serialize(file), cancellationToken);
        workspace.LastSyncedAt = DateTime.UtcNow;
    }

    /// <summary>Reads a repo's config file, if present, into a transient Workspace.</summary>
    public async Task<Workspace?> ReadAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var path = ConfigPathFor(repositoryRoot);
        if (!File.Exists(path)) return null;

        var yaml = await File.ReadAllTextAsync(path, cancellationToken);
        var file = Deserializer.Deserialize<WorkspaceConfigFile>(yaml);
        if (file is null) return null;

        var ws = new Workspace
        {
            Name = file.Name,
            AbsolutePath = repositoryRoot,
            DefaultBranch = file.DefaultBranch,
            CommandRules = file.AutoApprove.Select(a => new CommandRule
            {
                Pattern = a.Pattern,
                MatchType = Enum.TryParse<Core.RuleMatchType>(a.Match, true, out var m) ? m : Core.RuleMatchType.Prefix,
                Action = Enum.TryParse<Core.RuleAction>(a.Action, true, out var ac) ? ac : Core.RuleAction.AutoApprove
            }).ToList()
        };
        return ws;
    }
}
