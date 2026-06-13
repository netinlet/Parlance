using Microsoft.CodeAnalysis;
using Parlance.Abstractions;

namespace Parlance.CSharp.Workspace;

/// <summary>Roslyn span → <see cref="RepoPath"/> helpers for DTO/tool construction.</summary>
public static class FileLinePositionSpanExtensions
{
    /// <summary>The span's file path as a <see cref="RepoPath"/>, or null when the path is
    /// empty (e.g. metadata/synthetic locations).</summary>
    public static RepoPath? ToRepoPath(this FileLinePositionSpan span) => span.Path.ToRepoPath();
}
