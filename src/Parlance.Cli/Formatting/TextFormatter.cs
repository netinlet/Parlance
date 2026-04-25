using System.Text;
using Parlance.Analysis;

namespace Parlance.Cli.Formatting;

internal sealed class TextFormatter : IOutputFormatter
{
    public string Format(FileAnalysisResult result)
    {
        var sb = new StringBuilder();

        foreach (var diag in result.Diagnostics)
        {
            sb.AppendLine($"{diag.FilePath}({diag.Line},{diag.Column}): {diag.Severity} {diag.RuleId}: {diag.Message}");
            if (diag.Rationale is not null)
                sb.AppendLine($"  Rationale: {diag.Rationale}");
            if (diag.FixClassification is not null)
                sb.AppendLine($"  Fix: {diag.FixClassification}");
        }

        sb.AppendLine();
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"Total diagnostics: {result.Summary.TotalDiagnostics}");
        sb.AppendLine($"  Errors: {result.Summary.Errors}");
        sb.AppendLine($"  Warnings: {result.Summary.Warnings}");
        sb.AppendLine($"  Suggestions: {result.Summary.Suggestions}");
        sb.AppendLine($"Idiomatic score: {result.Summary.IdiomaticScore:F0}/100");

        return sb.ToString();
    }
}
