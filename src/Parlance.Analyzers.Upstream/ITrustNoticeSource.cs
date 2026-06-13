using System.Collections.Immutable;

namespace Parlance.Analyzers.Upstream;

/// <summary>
/// Implemented by external <see cref="IAnalyzerSource"/>s that can surface human-readable
/// trust notices (untrusted / hash-mismatched DLLs) without loading any analyzer code.
/// </summary>
public interface ITrustNoticeSource
{
    ImmutableList<string> GetTrustNotices(string repoPath);
}
