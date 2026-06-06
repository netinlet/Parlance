namespace Parlance.Abstractions;

/// <summary>
/// Maps <see cref="DiagnosticSeverity"/> to and from its stable lowercase wire
/// representation ("error" / "warning" / "suggestion" / "silent"). Serialization
/// is a boundary concern, so the pipeline carries the enum and only stringifies here.
/// </summary>
public static class DiagnosticSeverityFormatting
{
    extension(DiagnosticSeverity severity)
    {
        public string ToWireString() => severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Suggestion => "suggestion",
            DiagnosticSeverity.Silent => "silent",
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
        };
    }

    extension(DiagnosticSeverity)
    {
        public static DiagnosticSeverity FromWireString(string wire) => wire switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "suggestion" => DiagnosticSeverity.Suggestion,
            "silent" => DiagnosticSeverity.Silent,
            _ => throw new ArgumentOutOfRangeException(nameof(wire), wire, "Unknown severity wire value")
        };
    }
}
