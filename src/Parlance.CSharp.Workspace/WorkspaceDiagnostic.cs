namespace Parlance.CSharp.Workspace;

public sealed record WorkspaceDiagnostic(
    string Code,
    string Message,
    WorkspaceDiagnosticSeverity Severity);
