using System.CommandLine;
using System.Text.Json;
using Parlance.Analyzers.Upstream;

namespace Parlance.Cli.Commands;

internal static class RulesCommand
{
    private sealed record RuleInfo(string Id, string Title, string Category, string DefaultSeverity, bool HasFix);

    public static Command Create()
    {
        var categoryOption = new Option<string?>("--category") { Description = "Filter by category" };
        var severityOption = new Option<string?>("--severity") { Description = "Filter by severity" };
        var fixableOption = new Option<bool>("--fixable") { Description = "Show only rules with auto-fixes" };
        var formatOption = new Option<string>("--format", "-f") { Description = "Output format: text, json" };
        formatOption.DefaultValueFactory = _ => "text";
        formatOption.AcceptOnlyFromAmong("text", "json");

        var command = new Command("rules", "List available analysis rules");
        command.Add(categoryOption);
        command.Add(severityOption);
        command.Add(fixableOption);
        command.Add(formatOption);

        command.SetAction((parseResult, _) =>
        {
            var category = parseResult.GetValue(categoryOption);
            var severity = parseResult.GetValue(severityOption);
            var fixable = parseResult.GetValue(fixableOption);
            var format = parseResult.GetValue(formatOption)!;

            var rules = GetRules();

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

    private static List<RuleInfo> GetRules()
    {
        var analyzers = AnalyzerLoader.LoadAll("net10.0");
        var fixableIds = FixProviderLoader.LoadAll("net10.0")
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
                    fixableIds.Contains(descriptor.Id)));
            }
        }

        return rules.OrderBy(r => r.Id).ToList();
    }
}
