using System.Collections.Immutable;
using System.CommandLine;
using System.Text.Json;
using Parlance.Analyzers.Upstream;

namespace Parlance.Cli.Commands;

internal static class RulesCommand
{
    private sealed record RuleInfo(
        string Id,
        string Title,
        string Category,
        string DefaultSeverity,
        bool HasFix,
        string SeverityRaw,
        string MessageFormat,
        bool IsEnabledByDefault,
        string? HelpLinkUri,
        ImmutableList<string> CustomTags);

    public static Command Create()
    {
        var categoryOption = new Option<string?>("--category") { Description = "Filter by category" };
        var severityOption = new Option<string?>("--severity") { Description = "Filter by severity" };
        var fixableOption = new Option<bool>("--fixable") { Description = "Show only rules with auto-fixes" };
        var formatOption = new Option<string>("--format", "-f") { Description = "Output format: text, json" };
        formatOption.DefaultValueFactory = _ => "text";
        formatOption.AcceptOnlyFromAmong("text", "json");
        var analyzerOption = new Option<string[]>("--analyzer")
        {
            Description = "Path to an analyzer DLL or a directory of built analyzer DLLs to enumerate instead of " +
                          "Parlance's bundled rules. Repeatable. Point at the build output directory to include split CodeFixes assemblies.",
            AllowMultipleArgumentsPerToken = true
        };

        var command = new Command("rules", "List available analysis rules");
        command.Add(categoryOption);
        command.Add(severityOption);
        command.Add(fixableOption);
        command.Add(formatOption);
        command.Add(analyzerOption);

        command.SetAction((parseResult, _) =>
        {
            var category = parseResult.GetValue(categoryOption);
            var severity = parseResult.GetValue(severityOption);
            var fixable = parseResult.GetValue(fixableOption);
            var format = parseResult.GetValue(formatOption)!;
            var analyzerPaths = parseResult.GetValue(analyzerOption) ?? [];

            List<RuleInfo> rules;
            try
            {
                rules = GetRules(analyzerPaths);
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = 1;
                return Task.CompletedTask;
            }

            if (category is not null)
                rules = rules.Where(r => r.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
            if (severity is not null)
                rules = rules.Where(r => r.DefaultSeverity.Equals(severity, StringComparison.OrdinalIgnoreCase)).ToList();
            if (fixable)
                rules = rules.Where(r => r.HasFix).ToList();

            if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write(JsonSerializer.Serialize(rules, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));
            }
            else
            {
                Console.WriteLine($"{"ID",-12} {"Severity",-12} {"Category",-20} {"Fix",-5} {"Title"}");
                Console.WriteLine(new string('-', 80));
                foreach (var rule in rules)
                    Console.WriteLine($"{rule.Id,-12} {rule.DefaultSeverity,-12} {rule.Category,-20} {(rule.HasFix ? "Yes" : ""),-5} {rule.Title}");
                Console.WriteLine();
                Console.WriteLine($"{rules.Count} rule(s)");
            }

            return Task.CompletedTask;
        });

        return command;
    }

    private static List<RuleInfo> GetRules(IReadOnlyList<string> analyzerPaths)
    {
        var useExternal = analyzerPaths.Count > 0;

        var analyzers = useExternal
            ? AnalyzerLoader.LoadFromPaths(analyzerPaths)
            : AnalyzerLoader.LoadAll("net10.0");

        var fixProviders = useExternal
            ? FixProviderLoader.LoadFromPaths(analyzerPaths)
            : FixProviderLoader.LoadAll("net10.0");
        var fixableIds = fixProviders
            .SelectMany(fp => fp.FixableDiagnosticIds)
            .ToHashSet();

        var seenIds = new HashSet<string>();
        var rules = new List<RuleInfo>();

        foreach (var analyzer in analyzers)
        {
            foreach (var descriptor in analyzer.SupportedDiagnostics)
            {
                if (!seenIds.Add(descriptor.Id)) continue;

                var severity = descriptor.DefaultSeverity switch
                {
                    Microsoft.CodeAnalysis.DiagnosticSeverity.Error => "Error",
                    Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => "Warning",
                    Microsoft.CodeAnalysis.DiagnosticSeverity.Info => "Suggestion",
                    _ => "Silent",
                };

                rules.Add(new RuleInfo(
                    descriptor.Id,
                    descriptor.Title.ToString(),
                    descriptor.Category,
                    severity,
                    fixableIds.Contains(descriptor.Id),
                    descriptor.DefaultSeverity.ToString(),
                    descriptor.MessageFormat.ToString(),
                    descriptor.IsEnabledByDefault,
                    string.IsNullOrEmpty(descriptor.HelpLinkUri) ? null : descriptor.HelpLinkUri,
                    descriptor.CustomTags.ToImmutableList()));
            }
        }

        return rules.OrderBy(r => r.Id).ToList();
    }
}
