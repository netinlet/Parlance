using System.Text.Json;
using System.Text.Json.Serialization;

namespace Parlance.Cli.Formatting;

internal sealed class JsonFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Format(AnalysisOutput output)
    {
        var payload = new
        {
            diagnostics = output.Diagnostics.Select(fd => new
            {
                filePath = fd.FilePath,
                ruleId = fd.Diagnostic.RuleId,
                category = fd.Diagnostic.Category,
                severity = fd.Diagnostic.Severity.ToString().ToLowerInvariant(),
                message = fd.Diagnostic.Message,
                location = new
                {
                    line = fd.Diagnostic.Location.Line,
                    column = fd.Diagnostic.Location.Column,
                    endLine = fd.Diagnostic.Location.EndLine,
                    endColumn = fd.Diagnostic.Location.EndColumn
                },
                rationale = fd.Diagnostic.Rationale,
                suggestedFix = fd.Diagnostic.SuggestedFix
            }),
            summary = new
            {
                filesAnalyzed = output.FilesAnalyzed,
                totalDiagnostics = output.Summary.TotalDiagnostics,
                errors = output.Summary.Errors,
                warnings = output.Summary.Warnings,
                suggestions = output.Summary.Suggestions,
                byCategory = output.Summary.ByCategory,
                idiomaticScore = output.Summary.IdiomaticScore
            }
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }
}
