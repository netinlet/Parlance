using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.Analysis;

internal static class CompilationAnalyzerExtensions
{
    // Runs the analyzers against the compilation using the owning project's AnalyzerOptions, so the project's
    // .editorconfig-derived IDE style options (csharp_style_var_*, expression-bodied prefs, …) actually reach
    // the analyzers. The parameterless WithAnalyzers(analyzers) overload supplies empty AnalyzerOptions, which
    // strands every style preference on Roslyn's built-in defaults regardless of what the .editorconfig says.
    //
    // This is the configuration *input* to analysis (how analyzers evaluate, producing raw diagnostics). It is
    // independent of the CurationSet stage, which is a pure post-filter over already-emitted diagnostics.
    public static CompilationWithAnalyzers WithProjectAnalyzers(
        this Compilation compilation, ImmutableArray<DiagnosticAnalyzer> analyzers, Project project) =>
        compilation.WithAnalyzers(analyzers, project.AnalyzerOptions);
}
