using System.CommandLine;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.CSharp.Analyzers.Rules;

namespace Parlance.Cli.Commands;

internal static class RulesCommand
{
    private sealed record RuleInfo(
        string Id,
        string Title,
        string Category,
        string DefaultSeverity,
        bool HasFix);

    private static readonly string[] FixableRuleIds = ["PARL0004", "PARL9001"];

    private static readonly DiagnosticAnalyzer[] AllAnalyzers =
    [
        new PARL0001_PreferPrimaryConstructors(),
        new PARL0002_PreferCollectionExpressions(),
        new PARL0003_PreferRequiredProperties(),
        new PARL0004_UsePatternMatchingOverIsCast(),
        new PARL0005_UseSwitchExpression(),
        new PARL9001_UseSimpleUsingDeclaration(),
        new PARL9002_UseImplicitObjectCreation(),
        new PARL9003_UseDefaultLiteral(),
    ];

    public static Command Create()
    {
        var categoryOption = new Option<string?>("--category") { Description = "Filter by category" };
        var severityOption = new Option<string?>("--severity") { Description = "Filter by severity (Error, Warning, Suggestion)" };
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

            var output = format.ToLowerInvariant() switch
            {
                "json" => JsonSerializer.Serialize(rules, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }),
                "text" => FormatAsText(rules),
                _ => throw new InvalidOperationException($"Unexpected format value: '{format}'"),
            };

            Console.Write(output);

            return Task.CompletedTask;
        });

        return command;
    }

    private static string FormatAsText(List<RuleInfo> rules)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"ID",-12} {"Severity",-12} {"Category",-20} {"Fix",-5} {"Title"}");
        sb.AppendLine(new string('-', 80));
        foreach (var rule in rules)
        {
            sb.AppendLine($"{rule.Id,-12} {rule.DefaultSeverity,-12} {rule.Category,-20} {(rule.HasFix ? "Yes" : ""),-5} {rule.Title}");
        }
        sb.AppendLine();
        sb.AppendLine($"{rules.Count} rule(s)");
        return sb.ToString();
    }

    private static List<RuleInfo> GetRules()
    {
        var rules = new List<RuleInfo>();

        foreach (var analyzer in AllAnalyzers)
        {
            foreach (var descriptor in analyzer.SupportedDiagnostics)
            {
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
                    FixableRuleIds.Contains(descriptor.Id)));
            }
        }

        return rules.OrderBy(r => r.Id).ToList();
    }
}
