using System.Text;

namespace Parlance.Cli.Formatting;

internal sealed class TextFormatter : IOutputFormatter
{
    public string Format(AnalysisOutput output)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < output.Diagnostics.Count; i++)
        {
            if (i > 0)
                sb.AppendLine();

            var fileDiag = output.Diagnostics[i];
            var diag = fileDiag.Diagnostic;
            var loc = diag.Location;
            var severity = diag.Severity.ToString().ToLowerInvariant();

            sb.AppendLine($"{fileDiag.FilePath}({loc.Line},{loc.Column}): {severity} {diag.RuleId}: {diag.Message}");

            if (diag.Rationale is not null)
                sb.AppendLine($"  Rationale: {diag.Rationale}");

            if (diag.SuggestedFix is not null)
                sb.AppendLine($"  Suggested: {diag.SuggestedFix}");
        }

        sb.AppendLine();
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"Files analyzed: {output.FilesAnalyzed}");
        sb.AppendLine($"Total diagnostics: {output.Summary.TotalDiagnostics}");
        sb.AppendLine($"  Errors: {output.Summary.Errors}");
        sb.AppendLine($"  Warnings: {output.Summary.Warnings}");
        sb.AppendLine($"  Suggestions: {output.Summary.Suggestions}");
        sb.AppendLine($"Idiomatic score: {output.Summary.IdiomaticScore:F0}/100");

        return sb.ToString();
    }
}
