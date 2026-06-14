using System.Collections.Immutable;

namespace Parlance.Analyzers.Upstream;

/// <summary>
/// Implemented by external <see cref="IAnalyzerSource"/>s that can surface human-readable
/// trust notices (untrusted / hash-mismatched DLLs) without loading any analyzer code.
/// </summary>
public interface ITrustNoticeSource
{
    ImmutableList<string> GetTrustNotices(string repoPath);

    /// <summary>
    /// A cheap fingerprint of this source's trust state for <paramref name="repoPath"/>. It must
    /// change whenever a grant/revoke (or hash change) would alter what <c>Load</c> or
    /// <see cref="GetTrustNotices"/> returns, so aggregators can fold it into a cache key that
    /// self-invalidates on an out-of-band <c>parlance trust</c> change without a restart.
    /// </summary>
    string TrustFingerprint(string repoPath);
}
