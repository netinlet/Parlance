namespace Parlance.Abstractions;

public sealed record Location(
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string? FilePath = null);
