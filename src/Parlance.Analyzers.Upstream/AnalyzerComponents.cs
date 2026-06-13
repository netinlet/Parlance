using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.Analyzers.Upstream;

/// <summary>
/// What a single <see cref="IAnalyzerSource"/> contributes. Merging across sources is a
/// priority-ordered set union (see <see cref="AnalyzerProvider"/>), never plain concatenation.
/// </summary>
public sealed record AnalyzerComponents(
    ImmutableArray<DiagnosticAnalyzer> Analyzers,
    ImmutableArray<CodeFixProvider> FixProviders,
    ImmutableArray<CodeRefactoringProvider> RefactoringProviders)
{
    public static readonly AnalyzerComponents Empty = new([], [], []);
}
