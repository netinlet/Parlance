namespace Parlance.Analyzers.Upstream.Tests;

public sealed class GlobalDirectoryAnalyzerSourceTests
{
    private static string BundledAnalyzerDll =>
        Path.Combine(AppContext.BaseDirectory, "analyzer-dlls", "net10.0", "Parlance.CSharp.Analyzers.dll");

    // Creates a temp home dir with ~/.parlance/analyzers/local/ populated with the given DLL sources.
    // Returns (globalDir, trustFilePath, tempHomeDir).
    private static (string globalDir, string trustFilePath, string tempHome) MakeHomeWith(params string[] dllSourcePaths)
    {
        var home = Directory.CreateTempSubdirectory("parlance-global-home-").FullName;
        var globalDir = Path.Combine(home, ".parlance", "analyzers", "local");
        Directory.CreateDirectory(globalDir);
        foreach (var src in dllSourcePaths)
            File.Copy(src, Path.Combine(globalDir, Path.GetFileName(src)));
        var trustFilePath = Path.Combine(home, ".parlance", "trusted_analyzers.json");
        return (globalDir, trustFilePath, home);
    }

    private static AnalyzerTrustFile TrustAll(string globalDir, string trustFilePath)
    {
        var trust = new AnalyzerTrustFile(trustFilePath);
        trust.TrustDirectory(globalDir);
        return trust;
    }

    [Fact]
    public void Metadata_IsExternalPriority50()
    {
        Assert.Equal("global", new GlobalDirectoryAnalyzerSource().Name);
        Assert.Equal(SourceTrust.External, new GlobalDirectoryAnalyzerSource().Trust);
        Assert.Equal(50, new GlobalDirectoryAnalyzerSource().Priority);
    }

    [Fact]
    public void Load_MissingDirectory_ReturnsEmpty()
    {
        var home = Directory.CreateTempSubdirectory("parlance-global-missing-").FullName;
        try
        {
            var globalDir = Path.Combine(home, ".parlance", "analyzers", "local");
            var trustFilePath = Path.Combine(home, ".parlance", "trusted_analyzers.json");
            var source = new GlobalDirectoryAnalyzerSource(globalDir, trustFilePath);
            var result = source.Load("net10.0", home);
            Assert.Equal(AnalyzerComponents.Empty, result.Components);
            Assert.Empty(result.Failures);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void Load_UntrustedDll_ReturnsFailureNotAnalyzer()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var (globalDir, trustFilePath, home) = MakeHomeWith(BundledAnalyzerDll);
        try
        {
            var source = new GlobalDirectoryAnalyzerSource(globalDir, trustFilePath);
            var result = source.Load("net10.0", home);
            Assert.Empty(result.Components.Analyzers);
            Assert.Contains(result.Failures, f => f.Reason.Contains("Not trusted"));
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void Load_TrustedDll_ReturnsAnalyzers()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var (globalDir, trustFilePath, home) = MakeHomeWith(BundledAnalyzerDll);
        try
        {
            TrustAll(globalDir, trustFilePath);
            var source = new GlobalDirectoryAnalyzerSource(globalDir, trustFilePath);
            var result = source.Load("net10.0", home);
            Assert.NotEmpty(result.Components.Analyzers);
            Assert.Empty(result.Failures);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void Load_HashMismatch_ReturnsFailure()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var (globalDir, trustFilePath, home) = MakeHomeWith(BundledAnalyzerDll);
        try
        {
            TrustAll(globalDir, trustFilePath);
            var dllInHome = Path.Combine(globalDir, "Parlance.CSharp.Analyzers.dll");
            var bytes = File.ReadAllBytes(dllInHome);
            bytes[^1] ^= 0xFF; // flip last byte
            File.WriteAllBytes(dllInHome, bytes);

            var source = new GlobalDirectoryAnalyzerSource(globalDir, trustFilePath);
            var result = source.Load("net10.0", home);
            Assert.Empty(result.Components.Analyzers);
            Assert.Contains(result.Failures, f => f.Reason.Contains("Checksum mismatch"));
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void Probe_ListsDllsWithoutLoading()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var (globalDir, trustFilePath, home) = MakeHomeWith(BundledAnalyzerDll);
        try
        {
            var source = new GlobalDirectoryAnalyzerSource(globalDir, trustFilePath);
            var dlls = source.Probe(home);
            Assert.Single(dlls);
            Assert.EndsWith("Parlance.CSharp.Analyzers.dll", dlls[0]);
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void GetTrustNotices_UntrustedDll_ReturnsNotice()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var (globalDir, trustFilePath, home) = MakeHomeWith(BundledAnalyzerDll);
        try
        {
            var source = new GlobalDirectoryAnalyzerSource(globalDir, trustFilePath);
            var notices = source.GetTrustNotices(home);
            Assert.Contains(notices, n => n.StartsWith("Not trusted"));
        }
        finally { Directory.Delete(home, recursive: true); }
    }

    [Fact]
    public void GetTrustNotices_TrustedDll_ReturnsEmpty()
    {
        Assert.True(File.Exists(BundledAnalyzerDll));
        var (globalDir, trustFilePath, home) = MakeHomeWith(BundledAnalyzerDll);
        try
        {
            TrustAll(globalDir, trustFilePath);
            var source = new GlobalDirectoryAnalyzerSource(globalDir, trustFilePath);
            var notices = source.GetTrustNotices(home);
            Assert.Empty(notices);
        }
        finally { Directory.Delete(home, recursive: true); }
    }
}
