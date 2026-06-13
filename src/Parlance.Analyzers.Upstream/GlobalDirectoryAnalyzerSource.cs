namespace Parlance.Analyzers.Upstream;

/// <summary>
/// External source that loads analyzer DLLs from <c>~/.parlance/analyzers/local/</c>, gated by
/// <c>~/.parlance/trusted_analyzers.json</c>. The directory and trust file are machine-fixed (not
/// repo-relative). See <see cref="ExternalDirectoryAnalyzerSource"/> for the trust/scan behavior.
/// </summary>
public sealed class GlobalDirectoryAnalyzerSource : ExternalDirectoryAnalyzerSource
{
    private readonly string _globalDir;
    private readonly string _trustFilePath;

    public GlobalDirectoryAnalyzerSource()
    {
        _globalDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".parlance", "analyzers", "local");
        _trustFilePath = AnalyzerTrustFile.GlobalPath;
    }

    /// <summary>Internal constructor for testing — overrides both the global dir and trust file path.</summary>
    internal GlobalDirectoryAnalyzerSource(string globalDir, string trustFilePath)
    {
        _globalDir = globalDir;
        _trustFilePath = trustFilePath;
    }

    public override string Name => "global";
    public override int Priority => 50;

    protected override string ResolveDirectory(string repoPath) => _globalDir;

    protected override string ResolveTrustFile(string repoPath) => _trustFilePath;
}
