using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WheelHouse.Core.Agents;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// A task orchestration service implementing the dynamic cost-cascade pattern.
/// It attempts tasks first using the cheap tier (Gemini), runs verification, and
/// automatically escalates to the frontier tier (Claude Code) if verification fails.
/// When the workspace is a git repository the cheap tier runs in an isolated linked
/// worktree that is merged back only on verified success, so a failed attempt never
/// touches the user's working tree. Outside a repository, every file the cheap tier
/// writes is snapshotted first and exactly those files are restored/deleted on failure.
/// </summary>
public class CascadeOrchestrationService : ITaskOrchestrationService
{
    private readonly ITaskOrchestrationService _claudeService;
    private readonly IGeminiService _geminiService;
    private readonly IGitService _gitService;
    private readonly IVerificationRunner _verificationRunner;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>One cheap-tier file write, with enough state to undo it.</summary>
    private sealed record AppliedEdit(string FullPath, bool ExistedBefore, string? OriginalContent);

    public CascadeOrchestrationService(
        [FromKeyedServices("ClaudeCode")] ITaskOrchestrationService claudeService,
        IGeminiService geminiService,
        IGitService gitService,
        IVerificationRunner verificationRunner)
    {
        _claudeService = claudeService;
        _geminiService = geminiService;
        _gitService = gitService;
        _verificationRunner = verificationRunner;
    }

    public async IAsyncEnumerable<AgentStreamEvent> RunAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.VerificationCommand))
        {
            yield return new AgentStreamEvent(AgentEventKind.System, "Cascade: No verification command specified. Routing task directly to Claude Code.");
            await foreach (var evt in _claudeService.RunAsync(request, cancellationToken))
            {
                yield return evt;
            }
            yield break;
        }

        var cheapEvents = new List<AgentStreamEvent>();
        cheapEvents.Add(new AgentStreamEvent(AgentEventKind.System, "Cascade: Attempting task resolution using cheap tier (Gemini)..."));

        var workspaceRoot = Path.GetFullPath(request.WorkingDirectory);

        // Isolation: run the cheap tier in a linked worktree when the workspace is a git repo,
        // so a failed attempt never dirties the user's working tree. Outside a repo (or if the
        // worktree can't be created) fall back to snapshot-and-restore of exactly the files
        // the cheap tier writes.
        string executionDir = workspaceRoot;
        string? worktreePath = null;
        string? worktreeBranch = null;

        bool isRepo;
        try
        {
            isRepo = await _gitService.IsRepositoryAsync(workspaceRoot, cancellationToken);
        }
        catch
        {
            isRepo = false;
        }

        if (isRepo)
        {
            var runTag = $"{DateTime.UtcNow:HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
            var branch = $"wheelhouse/cascade-{runTag}";
            var path = Path.Combine(Path.GetTempPath(), "wheelhouse-worktrees", $"cascade-{runTag}");
            GitResult add;
            try
            {
                add = await _gitService.AddWorktreeAsync(workspaceRoot, path, branch, cancellationToken);
            }
            catch (Exception ex)
            {
                add = new GitResult(false, ex.Message);
            }

            if (add.Success)
            {
                worktreePath = path;
                worktreeBranch = branch;
                executionDir = path;
                cheapEvents.Add(new AgentStreamEvent(AgentEventKind.System, $"Cascade: Cheap tier isolated in worktree {path} ({branch})."));
            }
            else
            {
                cheapEvents.Add(new AgentStreamEvent(AgentEventKind.System, $"Cascade: Could not create isolation worktree ({add.Output.Trim()}). Falling back to per-file rollback in the workspace."));
            }
        }

        var appliedEdits = new Dictionary<string, AppliedEdit>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        bool cheapSuccess = false;
        bool preserveBranch = false;
        try
        {
            // Step 1: Ask Gemini to identify which files need to be read or edited
            var identifyPrompt =
                $"You are a code analysis helper. Given the following user task in a repository:\n" +
                $"---\n{request.Prompt}\n---\n" +
                $"Which files in the repository do you need to read or edit to complete this task?\n" +
                $"Return the relative file paths as a JSON array of strings. Do not include any other text, explanations, or markdown. Only return the JSON array.\n" +
                $"Example:\n[\"src/Common.cs\", \"tests/CommonTests.cs\"]";

            var identifyResponse = await _geminiService.CompleteAsync(identifyPrompt, cancellationToken);
            var files = ParseFileList(identifyResponse);

            // Step 2: Read target files from disk (workspace-contained paths only)
            var contextBuilder = new StringBuilder();
            foreach (var file in files)
            {
                if (!TryResolveWorkspacePath(executionDir, file, out var fullPath))
                {
                    cheapEvents.Add(new AgentStreamEvent(AgentEventKind.System, $"Cascade: Skipped file path outside workspace: {file}"));
                    continue;
                }
                if (File.Exists(fullPath))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                        contextBuilder.AppendLine($"FILE: {file}");
                        contextBuilder.AppendLine("CONTENT:");
                        contextBuilder.AppendLine(content);
                        contextBuilder.AppendLine("END_FILE");
                    }
                    catch (Exception ex)
                    {
                        cheapEvents.Add(new AgentStreamEvent(AgentEventKind.System, $"Cascade: Failed to read file {file}: {ex.Message}"));
                    }
                }
            }

            // Step 3: Ask Gemini to implement the changes
            var implementPrompt =
                $"You are a software engineer. Your task is to implement the following coding change in the repository:\n" +
                $"---\n{request.Prompt}\n---\n" +
                $"Here are the current contents of the files:\n" +
                $"{contextBuilder}\n" +
                $"Please implement the task. For each file you edit or create, output its full contents using this exact format:\n\n" +
                $"FILE: <relative path to file>\n" +
                $"CONTENT:\n" +
                $"<complete new file content>\n" +
                $"END_FILE\n\n" +
                $"Write the complete contents of the file, not just the changes. Do not include any explanation or other text.";

            var implementResponse = await _geminiService.CompleteAsync(implementPrompt, cancellationToken);
            var rejectedPaths = ApplyFileEdits(executionDir, implementResponse, appliedEdits);
            foreach (var rejected in rejectedPaths)
            {
                cheapEvents.Add(new AgentStreamEvent(AgentEventKind.System, $"Cascade: Rejected unsafe file path from model output: {rejected}"));
            }

            // Step 4: Verify the changes
            cheapEvents.Add(new AgentStreamEvent(AgentEventKind.System, $"Cascade: Cheap tier edits applied. Verifying changes with command: '{request.VerificationCommand}'..."));

            var verifyResult = await _verificationRunner.RunAsync(request.VerificationCommand, executionDir, cancellationToken: cancellationToken);
            if (verifyResult.Succeeded)
            {
                if (worktreePath is not null)
                {
                    // Verified success: snapshot the worktree edits and merge them into the workspace.
                    var status = await _gitService.GetStatusAsync(worktreePath, cancellationToken);
                    var committed = status.Clean;
                    if (!committed)
                    {
                        var commit = await _gitService.CommitAllAsync(worktreePath, "WheelHouse cascade: verified cheap-tier edits", cancellationToken);
                        committed = commit.Success;
                        if (!committed)
                        {
                            cheapEvents.Add(new AgentStreamEvent(AgentEventKind.Error, $"Cascade: Failed to commit cheap-tier edits: {commit.Output.Trim()}", IsError: true));
                        }
                    }

                    if (committed)
                    {
                        var merge = await _gitService.MergeBranchAsync(workspaceRoot, worktreeBranch!, cancellationToken);
                        if (merge.Success)
                        {
                            cheapSuccess = true;
                        }
                        else
                        {
                            preserveBranch = true;
                            cheapEvents.Add(new AgentStreamEvent(AgentEventKind.Error, $"Cascade: Merge of verified branch '{worktreeBranch}' failed — branch kept for manual resolution. {merge.Output.Trim()}", IsError: true));
                        }
                    }
                }
                else
                {
                    cheapSuccess = true;
                }

                if (cheapSuccess)
                {
                    cheapEvents.Add(new AgentStreamEvent(AgentEventKind.Result, "Cascade: Verification passed. Cheap tier implementation succeeded."));
                }
            }
            else
            {
                cheapEvents.Add(new AgentStreamEvent(AgentEventKind.System, $"Cascade: Cheap tier verification failed (exit code {verifyResult.ExitCode}). Reverting and escalating..."));
            }
        }
        catch (Exception ex)
        {
            cheapEvents.Add(new AgentStreamEvent(AgentEventKind.Error, $"Cascade: Cheap tier execution failed: {ex.Message}. Reverting and escalating...", IsError: true));
        }

        // Worktree cleanup (both outcomes): the workspace itself was never written to.
        if (worktreePath is not null)
        {
            try
            {
                await _gitService.RemoveWorktreeAsync(workspaceRoot, worktreePath, cancellationToken);
                if (!preserveBranch)
                {
                    await _gitService.DeleteBranchAsync(workspaceRoot, worktreeBranch!, force: !cheapSuccess, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                cheapEvents.Add(new AgentStreamEvent(AgentEventKind.System, $"Cascade: Worktree cleanup failed: {ex.Message}"));
            }
        }

        // Yield all accumulated cheap run events
        foreach (var evt in cheapEvents)
        {
            yield return evt;
        }

        // If cheap tier did not succeed, undo its edits and escalate to Claude Code
        if (!cheapSuccess)
        {
            yield return new AgentStreamEvent(AgentEventKind.System, "Cascade: Reverting cheap-tier edits and escalating to Claude Code...");

            if (worktreePath is null)
            {
                foreach (var failure in RollbackEdits(appliedEdits.Values))
                {
                    yield return new AgentStreamEvent(AgentEventKind.Error, $"Cascade: Failed to roll back {failure.FullPath}: {failure.Error}", IsError: true);
                }
            }

            await foreach (var evt in _claudeService.RunAsync(request, cancellationToken))
            {
                yield return evt;
            }
        }
    }

    public async Task<(int ExitCode, string Output)> RunVerificationAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = await _verificationRunner.RunAsync(command, workingDirectory, cancellationToken: cancellationToken);
        return (result.ExitCode, result.Output);
    }

    private static List<string> ParseFileList(string rawJson)
    {
        var cleaned = rawJson.Trim();
        if (cleaned.StartsWith("```"))
        {
            var idx = cleaned.IndexOf('\n');
            if (idx != -1) cleaned = cleaned[(idx + 1)..];
            if (cleaned.EndsWith("```")) cleaned = cleaned[..^3].Trim();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(cleaned, new JsonSerializerOptions { AllowTrailingCommas = true }) ?? new List<string>();
        }
        catch
        {
            // Fallback parsing if JSON deserialization fails
            var paths = new List<string>();
            var lines = cleaned.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim(' ', '"', '\'', '[', ']', ',');
                if (!string.IsNullOrWhiteSpace(trimmed)) paths.Add(trimmed);
            }
            return paths;
        }
    }

    /// <summary>
    /// Canonicalizes an LLM-supplied relative path and requires it to stay inside the
    /// workspace root; rooted paths and <c>..</c> escapes are rejected.
    /// </summary>
    private static bool TryResolveWorkspacePath(string workspaceRoot, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return false;

        var root = Path.GetFullPath(workspaceRoot);
        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        }
        catch
        {
            return false;
        }

        var rootWithSeparator = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, PathComparison)) return false;

        fullPath = candidate;
        return true;
    }

    /// <summary>
    /// Parses FILE/CONTENT/END_FILE blocks and writes each contained file, snapshotting prior
    /// state into <paramref name="appliedEdits"/> so failures can be rolled back precisely.
    /// Returns the paths that were rejected for escaping the workspace.
    /// </summary>
    private static List<string> ApplyFileEdits(string workingDirectory, string response, Dictionary<string, AppliedEdit> appliedEdits)
    {
        var rejected = new List<string>();
        var lines = response.Split('\n');
        string? currentFile = null;
        var currentContent = new StringBuilder();
        bool inContent = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("FILE:"))
            {
                // If there was an active file, write it first (handles nested structure issues)
                WriteActiveFile(workingDirectory, currentFile, currentContent, appliedEdits, rejected);

                currentFile = trimmed["FILE:".Length..].Trim();
                currentContent.Clear();
                inContent = false;
            }
            else if (trimmed == "CONTENT:")
            {
                inContent = true;
            }
            else if (trimmed == "END_FILE")
            {
                WriteActiveFile(workingDirectory, currentFile, currentContent, appliedEdits, rejected);
                currentFile = null;
                inContent = false;
            }
            else if (inContent)
            {
                currentContent.AppendLine(line);
            }
        }

        // Final safety check
        WriteActiveFile(workingDirectory, currentFile, currentContent, appliedEdits, rejected);
        return rejected;
    }

    private static void WriteActiveFile(string workingDirectory, string? file, StringBuilder content, Dictionary<string, AppliedEdit> appliedEdits, List<string> rejected)
    {
        if (file == null || content.Length == 0) return;

        if (!TryResolveWorkspacePath(workingDirectory, file, out var fullPath))
        {
            rejected.Add(file);
            return;
        }

        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null) Directory.CreateDirectory(dir);

        var contentText = content.ToString().Trim();
        if (contentText.StartsWith("```"))
        {
            var idx = contentText.IndexOf('\n');
            if (idx != -1) contentText = contentText[(idx + 1)..];
            if (contentText.EndsWith("```")) contentText = contentText[..^3].Trim();
        }

        // Snapshot the pre-edit state once per file so a failed run can restore it exactly.
        if (!appliedEdits.ContainsKey(fullPath))
        {
            var existed = File.Exists(fullPath);
            appliedEdits[fullPath] = new AppliedEdit(fullPath, existed, existed ? File.ReadAllText(fullPath) : null);
        }

        File.WriteAllText(fullPath, contentText);
    }

    /// <summary>Restores/deletes exactly the files the cheap tier wrote (non-worktree fallback).</summary>
    private static List<(string FullPath, string Error)> RollbackEdits(IEnumerable<AppliedEdit> edits)
    {
        var failures = new List<(string, string)>();
        foreach (var edit in edits)
        {
            try
            {
                if (edit.ExistedBefore)
                {
                    File.WriteAllText(edit.FullPath, edit.OriginalContent ?? string.Empty);
                }
                else if (File.Exists(edit.FullPath))
                {
                    File.Delete(edit.FullPath);
                }
            }
            catch (Exception ex)
            {
                failures.Add((edit.FullPath, ex.Message));
            }
        }
        return failures;
    }
}
