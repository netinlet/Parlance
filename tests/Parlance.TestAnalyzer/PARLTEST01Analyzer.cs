using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.TestAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARLTEST01Analyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARLTEST01";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Test Analyzer Rule",
        messageFormat: "PARLTEST01 triggered on '{0}'",
        category: "TestAnalyzer",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
    {
        var localDecl = (LocalDeclarationStatementSyntax)context.Node;

        foreach (var variable in localDecl.Declaration.Variables)
        {
            var name = variable.Identifier.Text;
            if (name.StartsWith("testTrigger"))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, variable.Identifier.GetLocation(), name));
            }
        }
    }
}
