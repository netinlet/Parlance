namespace Parlance.Abstractions;

public interface IAnalysisEngine
{
    string Language { get; }

    Task<AnalysisResult> AnalyzeSourceAsync(
        string sourceCode,
        AnalysisOptions? options = null,
        CancellationToken ct = default);
}
