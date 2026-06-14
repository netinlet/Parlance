namespace Parlance.Analyzers.Upstream.Tests;

public sealed class AnalyzerTrustFileTests
{
    private static string TempStorePath() =>
        Path.Combine(Directory.CreateTempSubdirectory("parlance-trust-").FullName, "trusted_analyzers.json");

    private static string WriteTempDll()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("parlance-dll-").FullName, "test.dll");
        File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A, 0x01, 0x02 }); // fake PE header
        return path;
    }

    [Fact]
    public void Check_MissingFile_ReturnsNotFound()
    {
        var store = new AnalyzerTrustFile(TempStorePath());
        Assert.Equal(TrustCheckResult.NotFound, store.Check("/any/path.dll"));
    }

    [Fact]
    public void Trust_ThenCheck_ReturnsTrusted()
    {
        var dll = WriteTempDll();
        var store = new AnalyzerTrustFile(TempStorePath());
        store.Trust(dll);
        Assert.Equal(TrustCheckResult.Trusted, store.Check(dll));
    }

    [Fact]
    public void Check_AfterDllChanges_ReturnsHashMismatch()
    {
        var dll = WriteTempDll();
        var store = new AnalyzerTrustFile(TempStorePath());
        store.Trust(dll);
        File.WriteAllBytes(dll, new byte[] { 0x4D, 0x5A, 0xFF, 0xFF }); // mutate
        Assert.Equal(TrustCheckResult.HashMismatch, store.Check(dll));
    }

    [Fact]
    public void TrustDirectory_ThenCheck_TrustsAllDlls()
    {
        var dir = Directory.CreateTempSubdirectory("parlance-dir-").FullName;
        var dll1 = Path.Combine(dir, "a.dll");
        var dll2 = Path.Combine(dir, "b.dll");
        File.WriteAllBytes(dll1, new byte[] { 0x4D, 0x5A, 0x01 });
        File.WriteAllBytes(dll2, new byte[] { 0x4D, 0x5A, 0x02 });
        var store = new AnalyzerTrustFile(TempStorePath());
        store.TrustDirectory(dir);
        Assert.Equal(TrustCheckResult.Trusted, store.Check(dll1));
        Assert.Equal(TrustCheckResult.Trusted, store.Check(dll2));
    }

    [Fact]
    public void Revoke_RemovesEntry()
    {
        var dll = WriteTempDll();
        var store = new AnalyzerTrustFile(TempStorePath());
        store.Trust(dll);
        store.Revoke(dll);
        Assert.Equal(TrustCheckResult.NotFound, store.Check(dll));
    }

    [Fact]
    public void Fingerprint_ChangesAfterWrite()
    {
        var dll = WriteTempDll();
        var storePath = TempStorePath();
        var store = new AnalyzerTrustFile(storePath);
        var before = store.Fingerprint();
        store.Trust(dll);
        var after = store.Fingerprint();
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void List_ReturnsAllEntries()
    {
        var dll = WriteTempDll();
        var store = new AnalyzerTrustFile(TempStorePath());
        store.Trust(dll);
        var entries = store.List();
        Assert.Single(entries);
        Assert.Equal(dll, entries[0].DllPath);
        Assert.StartsWith("sha256:", entries[0].Hash);
    }

    [Fact]
    public void TrustDirectory_SkipsResourcesDlls()
    {
        var dir = Directory.CreateTempSubdirectory("parlance-res-").FullName;
        var real = Path.Combine(dir, "Analyzer.dll");
        var satellite = Path.Combine(dir, "Analyzer.resources.dll");
        File.WriteAllBytes(real, new byte[] { 0x4D, 0x5A, 0x01 });
        File.WriteAllBytes(satellite, new byte[] { 0x4D, 0x5A, 0x02 });
        var store = new AnalyzerTrustFile(TempStorePath());

        store.TrustDirectory(dir);

        var paths = store.List().Select(e => e.DllPath).ToHashSet();
        Assert.Contains(Path.GetFullPath(real), paths);
        Assert.DoesNotContain(Path.GetFullPath(satellite), paths);
    }

    [Fact]
    public void Check_CorruptFile_FailsClosedAsNotFound()
    {
        var dll = WriteTempDll();
        var storePath = TempStorePath();
        File.WriteAllText(storePath, "{ this is not valid json");
        var store = new AnalyzerTrustFile(storePath);

        Assert.Equal(TrustCheckResult.NotFound, store.Check(dll));
    }

    [Fact]
    public void Trust_CorruptFile_ThrowsRatherThanDiscardingGrants()
    {
        var dll = WriteTempDll();
        var storePath = TempStorePath();
        File.WriteAllText(storePath, "{ corrupt");
        var store = new AnalyzerTrustFile(storePath);

        Assert.Throws<InvalidOperationException>(() => store.Trust(dll));
        // The corrupt file must be left intact, not overwritten with a fresh {}.
        Assert.Equal("{ corrupt", File.ReadAllText(storePath));
    }

    [Fact]
    public void ProjectPath_ReturnsCorrectRelativeLocation()
    {
        var path = AnalyzerTrustFile.ProjectPath("/repo/root");
        Assert.Equal("/repo/root/.parlance/trusted_analyzers.json", path);
    }

    [Fact]
    public void GlobalPath_IsUnderHomeDirectory()
    {
        Assert.Contains(".parlance", AnalyzerTrustFile.GlobalPath);
        Assert.EndsWith("trusted_analyzers.json", AnalyzerTrustFile.GlobalPath);
    }
}
