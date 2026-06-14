namespace Parlance.Analyzers.Upstream.Tests;

public sealed class AnalyzerDllScannerReportTests
{
    [Fact]
    public void ScanReport_GoodAndBadDll_LoadsGoodReportsBad()
    {
        var dir = Directory.CreateTempSubdirectory("parlance-scan-").FullName;
        try
        {
            // A real analyzer assembly (the bundled PARL analyzers DLL) copied in as "good".
            var good = Path.Combine(AppContext.BaseDirectory, "analyzer-dlls", "net10.0", "Parlance.CSharp.Analyzers.dll");
            Assert.True(File.Exists(good), $"Expected bundled analyzer DLL at {good}");
            File.Copy(good, Path.Combine(dir, "Parlance.CSharp.Analyzers.dll"));

            // A junk file with a .dll extension that is not a valid PE image — must be reported, not crash.
            File.WriteAllText(Path.Combine(dir, "broken.dll"), "this is not a PE file");

            var result = AnalyzerDllScanner.ScanAssembliesFromPathsReport([dir]);

            Assert.NotEmpty(result.Assemblies);
            Assert.Contains(result.Failures, f => f.DllPath.EndsWith("broken.dll"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
