using System.Text.Json;
using Parlance.Analyzers.Upstream;
using Parlance.ManifestGenerator;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Parlance.ManifestGenerator <tfm> [output-path]");
    Console.Error.WriteLine("  tfm: net8.0 or net10.0");
    Console.Error.WriteLine("  output-path: optional, defaults to rule-manifest-<tfm>.json");
    return 1;
}

var tfm = args[0];
var outputPath = args.Length > 1
    ? args[1]
    : $"rule-manifest-{tfm}.json";

Console.WriteLine($"Generating manifest for {tfm}...");

var analyzers = AnalyzerLoader.LoadAll(tfm);

var rules = new List<RuleEntry>();

foreach (var analyzer in analyzers)
{
    var assemblyName = analyzer.GetType().Assembly.GetName().Name ?? "unknown";

    foreach (var descriptor in analyzer.SupportedDiagnostics)
    {
        if (rules.Any(r => r.Id == descriptor.Id))
            continue;

        var severity = descriptor.DefaultSeverity switch
        {
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error => "error",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => "warning",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Info => "suggestion",
            Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden => "silent",
            _ => "silent",
        };

        rules.Add(new RuleEntry(
            Id: descriptor.Id,
            Title: descriptor.Title.ToString(),
            Category: descriptor.Category,
            DefaultSeverity: severity,
            Description: descriptor.Description.ToString(),
            HelpUrl: descriptor.HelpLinkUri,
            Source: assemblyName,
            HasRationale: false,
            HasSuggestedFix: false));
    }
}

rules.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));

var manifest = new RuleManifest(
    TargetFramework: tfm,
    GeneratedAt: DateTime.UtcNow.ToString("yyyy-MM-dd"),
    Rules: rules);

var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
});

File.WriteAllText(outputPath, json);
Console.WriteLine($"Wrote {rules.Count} rules to {outputPath}");

return 0;
