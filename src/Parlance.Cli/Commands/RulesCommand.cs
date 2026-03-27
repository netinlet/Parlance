using System.CommandLine;
using System.Text.Json;
using Microsoft.CodeAnalysis.CodeFixes;
using Parlance.Analyzers.Upstream;

namespace Parlance.Cli.Commands;

internal static class RulesCommand
{
    private sealed record RuleInfo(
        string Id,
        string Title,
        string Category,
        string DefaultSeverity,
        bool HasFix);

    public static Command Create()
    {
        var categoryOption = new Option<string?>("--category") { Description = "Filter by category" };
        var severityOption = new Option<string?>("--severity") { Description = "Filter by severity (Error, Warning, Suggestion)" };
        var fixableOption = new Option<bool>("--fixable") { Description = "Show only rules with auto-fixes" };
        var formatOption = new Option<string>("--format", "-f") { Description = "Output format: text, json" };
        formatOption.DefaultValueFactory = _ => "text";
        formatOption.AcceptOnlyFromAmong("text", "json");
        var tfmOption = new Option<string>("--target-framework") { Description = "Target framework for analyzer resolution" };
        tfmOption.DefaultValueFactory = _ => "net10.0";

        var command = new Command("rules", "List available analysis rules");
        command.Add(categoryOption);
        command.Add(severityOption);
        command.Add(fixableOption);
        command.Add(formatOption);
        command.Add(tfmOption);

        command.SetAction((parseResult, _) =>
        {
            var category = parseResult.GetValue(categoryOption);
            var severity = parseResult.GetValue(severityOption);
            var fixable = parseResult.GetValue(fixableOption);
            var format = parseResult.GetValue(formatOption)!;
            var targetFramework = parseResult.GetValue(tfmOption)!;

            var rules = GetRules(targetFramework);

            if (category is not null)
                rules = rules.Where(r => r.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
            if (severity is not null)
                rules = rules.Where(r => r.DefaultSeverity.Equals(severity, StringComparison.OrdinalIgnoreCase)).ToList();
            if (fixable)
                rules = rules.Where(r => r.HasFix).ToList();

            if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonSerializer.Serialize(rules, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                });
                Console.Write(json);
            }
            else
            {
                Console.WriteLine($"{"ID",-12} {"Severity",-12} {"Category",-20} {"Fix",-5} {"Title"}");
                Console.WriteLine(new string('-', 80));
                foreach (var rule in rules)
                {
                    Console.WriteLine($"{rule.Id,-12} {rule.DefaultSeverity,-12} {rule.Category,-20} {(rule.HasFix ? "Yes" : ""),-5} {rule.Title}");
                }
                Console.WriteLine();
                Console.WriteLine($"{rules.Count} rule(s)");
            }

            return Task.CompletedTask;
        });

        return command;
    }

    private static List<RuleInfo> GetRules(string targetFramework)
    {
        var analyzers = AnalyzerLoader.LoadAll(targetFramework);
        var fixableIds = DiscoverFixableIds();
        var rules = new List<RuleInfo>();
        var seenIds = new HashSet<string>();

        foreach (var analyzer in analyzers)
        {
            foreach (var descriptor in analyzer.SupportedDiagnostics)
            {
                if (!seenIds.Add(descriptor.Id))
                    continue;

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

    private static HashSet<string> DiscoverFixableIds()
    {
        var parlAssembly = typeof(Parlance.CSharp.Analyzers.Rules.PARL9003_UseDefaultLiteral).Assembly;
        return parlAssembly.DiscoverInstances<CodeFixProvider>()
            .SelectMany(fp => fp.FixableDiagnosticIds)
            .ToHashSet();
    }
}
