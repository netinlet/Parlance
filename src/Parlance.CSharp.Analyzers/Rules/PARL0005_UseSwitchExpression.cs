using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL0005_UseSwitchExpression : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL0005";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Use switch expression instead of switch statement",
        messageFormat: "This switch statement can be converted to a switch expression",
        category: "PatternMatching",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Switch expressions are more concise than switch statements when every branch returns a value. They enforce exhaustiveness and make data-flow intent clearer.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var parseOptions = compilationContext.Compilation.SyntaxTrees
                .FirstOrDefault()?.Options as CSharpParseOptions;

            if (parseOptions?.LanguageVersion < LanguageVersion.CSharp8)
                return;

            compilationContext.RegisterSyntaxNodeAction(AnalyzeSwitchStatement, SyntaxKind.SwitchStatement);
        });
    }

    private static void AnalyzeSwitchStatement(SyntaxNodeAnalysisContext context)
    {
        var switchStatement = (SwitchStatementSyntax)context.Node;

        if (switchStatement.Sections.Count == 0)
            return;

        var hasDefault = false;

        foreach (var section in switchStatement.Sections)
        {
            if (section.Labels.Any(l => l is DefaultSwitchLabelSyntax))
                hasDefault = true;

            if (!SectionOnlyReturns(section))
                return;
        }

        // Only flag if there's a default — otherwise it's not exhaustive
        if (!hasDefault)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            switchStatement.GetLocation()));
    }

    private static bool SectionOnlyReturns(SwitchSectionSyntax section)
    {
        var statements = section.Statements;
        var meaningful = statements
            .Where(s => s is not EmptyStatementSyntax)
            .ToList();

        return meaningful.Count == 1 && meaningful[0] is ReturnStatementSyntax;
    }
}
