namespace Parlance.Analyzers.Upstream;

internal static class AnalyzerTrustMessages
{
    internal static string? TrustFailureMessage(TrustCheckResult result, string dll) => result switch
    {
        TrustCheckResult.NotFound =>
            $"Not trusted — run 'parlance trust \"{dll}\"' to approve",
        TrustCheckResult.HashMismatch =>
            $"Checksum mismatch — DLL changed since trusted, re-run 'parlance trust \"{dll}\"'",
        _ => null
    };
}
