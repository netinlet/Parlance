namespace Parlance.Analyzers.Upstream.Tests;

public sealed class LocalDirectoryAnalyzerSourceTests
{
    private static string BundledAnalyzerDll =>
        Path.Combine(AppContext.BaseDirectory, "analyzer-dlls", "net10.0", "Parlance.CSharp.Analyzers.dll");

    // Creates a temp repo dir with .parlance/analyzers/local/ populated with the given DLL sources.
    private static string MakeRepoWith(params string[] dllSourcePaths)
    {
        var repo = Directory.CreateTempSubdirectory("parlance-local-").FullName;
        var localDir = Path.Combine(repo, ".parlance", "analyzers", "local");
        Directory.CreateDirectory(localDir);
        foreach (var src in dllSourcePaths)
            File.Copy(src, Path.Combine(localDir, Path.GetFileName(src)));
        return repo;
    }

    // Creates an AnalyzerTrustFile for a repo and trusts all DLLs in .parlance/analyzers/local/.
    private static AnalyzerTrustFile TrustAll(string repoDir)
    {
        var trust = new AnalyzerTrustFile(AnalyzerTrustFile.ProjectPath(repoDir));
        trust.TrustDirectory(Path.Combine(repoDir, ".parlance", "analyzers", "local"));
        return trust;
    }

    [Fact]
    public void Metadata_IsExternalPriority100()
    {
        Assert.Equal("local", new LocalDirectoryAnalyzerSource().Name);
        Assert.Equal(SourceTrust.External, new LocalDirectoryAnalyzerSource().Trust);
        Assert.Equal(100, new LocalDirectoryAnalyzerSource().Priority);
    }

    [Fact]
    public void Load_MissingDirectory_ReturnsEmpty()
    {
        var repo = Directory.CreateTempSubdirectory("parlance-missing-").FullName;
        try
        {
            var result = new LocalDirectoryAnalyzerSource().Load("net10.0", repo);
            Assert.Equal(AnalyzerComponents.Empty, result.Components);
            Assert.Empty(result.Failures);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void Load_UntrustedDll_ReturnsFailureNotAnalyzer()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var repo = MakeRepoWith(BundledAnalyzerDll);
        try
        {
            // No trust granted — load must NOT execute the DLL
            var result = new LocalDirectoryAnalyzerSource().Load("net10.0", repo);
            Assert.Empty(result.Components.Analyzers);
            Assert.Contains(result.Failures, f => f.Reason.Contains("Not trusted"));
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void Load_TrustedDll_ReturnsAnalyzers()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var repo = MakeRepoWith(BundledAnalyzerDll);
        try
        {
            TrustAll(repo);
            var result = new LocalDirectoryAnalyzerSource().Load("net10.0", repo);
            Assert.NotEmpty(result.Components.Analyzers);
            Assert.Empty(result.Failures);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void Load_HashMismatch_ReturnsFailure()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var repo = MakeRepoWith(BundledAnalyzerDll);
        try
        {
            TrustAll(repo);
            // Mutate the DLL after trust was recorded
            var dllInRepo = Path.Combine(repo, ".parlance", "analyzers", "local", "Parlance.CSharp.Analyzers.dll");
            var bytes = File.ReadAllBytes(dllInRepo);
            bytes[^1] ^= 0xFF; // flip last byte
            File.WriteAllBytes(dllInRepo, bytes);

            var result = new LocalDirectoryAnalyzerSource().Load("net10.0", repo);
            Assert.Empty(result.Components.Analyzers);
            Assert.Contains(result.Failures, f => f.Reason.Contains("Checksum mismatch"));
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void Load_BadDllAlongsideTrustedGood_GoodLoadsAndBadIsReported()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var repo = MakeRepoWith(BundledAnalyzerDll);
        // Add a junk DLL
        File.WriteAllText(Path.Combine(repo, ".parlance", "analyzers", "local", "broken.dll"), "junk");
        try
        {
            TrustAll(repo); // trusts both (broken.dll gets a hash of the junk content)
            var result = new LocalDirectoryAnalyzerSource().Load("net10.0", repo);
            Assert.NotEmpty(result.Components.Analyzers); // good DLL loaded
            Assert.Contains(result.Failures, f => f.DllPath.EndsWith("broken.dll")); // bad DLL reported
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void Probe_ListsDllsWithoutLoading()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var repo = MakeRepoWith(BundledAnalyzerDll);
        try
        {
            var dlls = new LocalDirectoryAnalyzerSource().Probe(repo);
            Assert.Single(dlls);
            Assert.EndsWith("Parlance.CSharp.Analyzers.dll", dlls[0]);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void GetTrustNotices_UntrustedDll_ReturnsNotice()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var repo = MakeRepoWith(BundledAnalyzerDll);
        try
        {
            var notices = new LocalDirectoryAnalyzerSource().GetTrustNotices(repo);
            Assert.Contains(notices, n => n.StartsWith("Not trusted"));
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void GetTrustNotices_TrustedDll_ReturnsEmpty()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var repo = MakeRepoWith(BundledAnalyzerDll);
        try
        {
            TrustAll(repo);
            var notices = new LocalDirectoryAnalyzerSource().GetTrustNotices(repo);
            Assert.Empty(notices);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }
}
