using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using Parlance.Analysis;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class AnalyzeDiffTool
{
    [McpServerTool(Name = "analyze-diff", ReadOnly = true)]
    [Description("Run diagnostics on C# files changed in a git ref range. Defaults to origin/main or main when baseRef is omitted.")]
    public static async Task<AnalyzeToolResult> AnalyzeDiff(
        WorkspaceSessionHolder holder,
        AnalysisService analysis,
        string? baseRef = null,
        string? curationSet = null,
        int? maxDiagnostics = null,
        CancellationToken ct = default)
    {
        if (holder.LoadFailure is { } failure)
            return AnalyzeToolResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return AnalyzeToolResult.NotLoaded();

        var session = holder.Session;
        var workspaceRoot = GetWorkspaceRoot(session.WorkspacePath);
        var resolvedBaseRef = string.IsNullOrWhiteSpace(baseRef)
            ? await DetectDefaultBaseRef(workspaceRoot, ct)
            : baseRef;

        var diff = await RunGit(workspaceRoot, ["diff", "--name-only", $"{resolvedBaseRef}...HEAD"], ct);
        if (diff.ExitCode != 0)
            return AnalyzeToolResult.Failed($"git diff failed: {diff.StandardError.Trim()}");

        var changedPaths = diff.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => string.Equals(Path.GetExtension(p), ".cs", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetFullPath(Path.Combine(workspaceRoot, p)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var files = session.CurrentSolution.Projects
            .SelectMany(p => p.Documents)
            .Select(d => d.FilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!))
            .Where(changedPaths.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableList();

        try
        {
            var options = new AnalyzeOptions(curationSet, maxDiagnostics);
            var result = await analysis.AnalyzeFilesAsync(files, options, ct);
            return ToToolResult(result);
        }
        catch (ArgumentException ex)
        {
            return AnalyzeToolResult.Failed(ex.Message);
        }
    }

    private static string GetWorkspaceRoot(string workspacePath) =>
        Path.GetDirectoryName(workspacePath) ?? workspacePath;

    private static async Task<string> DetectDefaultBaseRef(string workingDirectory, CancellationToken ct)
    {
        var originMain = await RunGit(workingDirectory, ["rev-parse", "--verify", "origin/main"], ct);
        if (originMain.ExitCode == 0)
            return "origin/main";

        return "main";
    }

    private static async Task<GitResult> RunGit(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new GitResult(process.ExitCode, stdout, stderr);
    }

    private static AnalyzeToolResult ToToolResult(FileAnalysisResult result) =>
        AnalyzeToolResult.Success(
            result.CurationSet,
            new AnalyzeSummary(
                result.Summary.TotalDiagnostics,
                result.Summary.Errors,
                result.Summary.Warnings,
                result.Summary.Suggestions,
                result.Summary.IdiomaticScore),
            result.Diagnostics.Select(d => new AnalyzeDiagnostic(
                d.RuleId, d.Severity, d.Message,
                d.FilePath, d.Line,
                d.FixClassification, d.Rationale)).ToImmutableList());

    private readonly record struct GitResult(int ExitCode, string StandardOutput, string StandardError);
}
