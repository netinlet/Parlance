namespace Parlance.Abstractions;

/// <summary>
/// Provides code analysis for a specific programming language.
/// Implementations produce scored diagnostics from source code or workspace files.
/// </summary>
public interface IAnalysisEngine
{
    /// <summary>Gets the language this engine supports (e.g. "C#").</summary>
    string Language { get; }

    /// <summary>
    /// Analyzes the provided source code and returns a scored result containing all diagnostics.
    /// </summary>
    /// <param name="sourceCode">The raw source code to analyze.</param>
    /// <param name="options">Optional analysis options to control severity thresholds and rule sets.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="AnalysisResult"/> containing diagnostics and a quality score.</returns>
    Task<AnalysisResult> AnalyzeSourceAsync(
        string sourceCode,
        AnalysisOptions? options = null,
        CancellationToken ct = default);
}
