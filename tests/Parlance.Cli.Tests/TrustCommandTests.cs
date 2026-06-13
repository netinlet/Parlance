using System.Reflection;
using Parlance.Analyzers.Upstream;
using Parlance.Cli.Commands;

namespace Parlance.Cli.Tests;

/// <summary>
/// Unit tests for TrustCommand covering the AnalyzerTrustFile interactions.
/// These tests call AnalyzerTrustFile directly and verify the underlying store,
/// rather than spawning a subprocess, so they run fast without a full CLI build.
/// </summary>
public sealed class TrustCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dllA;
    private readonly string _dllB;

    public TrustCommandTests()
    {
        _tempDir = Directory.CreateTempSubdirectory("parlance-trust-tests-").FullName;

        // Create dummy DLL files (just need to exist on disk with some bytes)
        _dllA = Path.Combine(_tempDir, "AnalyzerA.dll");
        _dllB = Path.Combine(_tempDir, "AnalyzerB.dll");
        File.WriteAllBytes(_dllA, new byte[] { 0x4D, 0x5A, 0x00, 0x01 }); // MZ header stub
        File.WriteAllBytes(_dllB, new byte[] { 0x4D, 0x5A, 0x00, 0x02 });
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string ProjectTrustFilePath() =>
        AnalyzerTrustFile.ProjectPath(_tempDir);

    private AnalyzerTrustFile ProjectTrustFile() =>
        new(ProjectTrustFilePath());

    private AnalyzerTrustFile GlobalTrustFile(string dir) =>
        new(Path.Combine(dir, "global_trusted_analyzers.json"));

    [Fact]
    public void Trust_SingleDll_WritesToProjectFile()
    {
        var trustFile = ProjectTrustFile();
        trustFile.Trust(_dllA);

        Assert.True(File.Exists(ProjectTrustFilePath()));
        var entries = trustFile.List();
        Assert.Single(entries);
        Assert.Equal(Path.GetFullPath(_dllA), entries[0].DllPath);
        Assert.StartsWith("sha256:", entries[0].Hash);
    }

    [Fact]
    public void Trust_Directory_TrustsAllDlls()
    {
        var trustFile = ProjectTrustFile();
        trustFile.TrustDirectory(_tempDir);

        var entries = trustFile.List();
        var paths = entries.Select(e => e.DllPath).ToHashSet();
        Assert.Contains(Path.GetFullPath(_dllA), paths);
        Assert.Contains(Path.GetFullPath(_dllB), paths);
        Assert.True(entries.Count >= 2);
    }

    [Fact]
    public void Trust_GlobalFlag_WritesToGlobalFile()
    {
        var globalDir = Path.Combine(_tempDir, "global");
        Directory.CreateDirectory(globalDir);
        var globalTrustFile = GlobalTrustFile(globalDir);

        globalTrustFile.Trust(_dllA);

        var entries = globalTrustFile.List();
        Assert.Single(entries);
        Assert.Equal(Path.GetFullPath(_dllA), entries[0].DllPath);

        // Project file should NOT be written
        Assert.False(File.Exists(ProjectTrustFilePath()));
    }

    [Fact]
    public void List_ShowsAllEntries()
    {
        var trustFile = ProjectTrustFile();
        trustFile.Trust(_dllA);
        trustFile.Trust(_dllB);

        var entries = trustFile.List();
        Assert.Equal(2, entries.Count);
        var paths = entries.Select(e => e.DllPath).ToList();
        Assert.Contains(Path.GetFullPath(_dllA), paths);
        Assert.Contains(Path.GetFullPath(_dllB), paths);
        Assert.All(entries, e => Assert.StartsWith("sha256:", e.Hash));
    }

    [Fact]
    public void List_Empty_ShowsEmptyMessage()
    {
        // File doesn't exist yet — List() should return empty
        var trustFile = ProjectTrustFile();
        var entries = trustFile.List();
        Assert.Empty(entries);
    }

    [Fact]
    public void Revoke_SingleDll_RemovesFromFile()
    {
        var trustFile = ProjectTrustFile();
        trustFile.Trust(_dllA);
        trustFile.Trust(_dllB);

        trustFile.Revoke(_dllA);

        var entries = trustFile.List();
        Assert.Single(entries);
        Assert.Equal(Path.GetFullPath(_dllB), entries[0].DllPath);
    }

    [Fact]
    public void Revoke_Directory_RevokesAllDlls()
    {
        var trustFile = ProjectTrustFile();
        trustFile.TrustDirectory(_tempDir);

        // Sanity: both are trusted before revoke
        var before = trustFile.List();
        Assert.True(before.Count >= 2);

        trustFile.RevokeDirectory(_tempDir);

        var after = trustFile.List();
        var paths = after.Select(e => e.DllPath).ToHashSet();
        Assert.DoesNotContain(Path.GetFullPath(_dllA), paths);
        Assert.DoesNotContain(Path.GetFullPath(_dllB), paths);
    }
}
