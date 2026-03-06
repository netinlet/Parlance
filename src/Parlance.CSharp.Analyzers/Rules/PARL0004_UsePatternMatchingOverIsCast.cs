using System.Linq;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL0004_UsePatternMatchingOverIsCast : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL0004";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Use pattern matching instead of 'is' followed by cast",
        messageFormat: "Use pattern matching 'is {0} name' instead of 'is' check followed by cast",
        category: "PatternMatching",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Pattern matching combines type checking and variable declaration in one expression, avoiding the redundant cast.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var parseOptions = compilationContext.Compilation.SyntaxTrees
                .FirstOrDefault()?.Options as CSharpParseOptions;

            if (parseOptions?.LanguageVersion < LanguageVersion.CSharp7)
                return;

            compilationContext.RegisterSyntaxNodeAction(AnalyzeIfStatement, SyntaxKind.IfStatement);
        });
    }

    private static void AnalyzeIfStatement(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;

        // Look for: if (expr is TypeName)
        if (ifStatement.Condition is not BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.IsExpression
            } isExpression)
        {
            return;
        }

        // Get the expression being checked and the type
        var checkedExpr = isExpression.Left;
        var targetType = isExpression.Right as TypeSyntax;
        if (targetType is null) return;

        // Look for a cast to the same type in the if-body
        var body = ifStatement.Statement;
        if (!ContainsCastOf(body, checkedExpr, targetType, context.SemanticModel))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            isExpression.GetLocation(),
            targetType.ToString()));
    }

    private static bool ContainsCastOf(
        SyntaxNode body,
        ExpressionSyntax checkedExpr,
        TypeSyntax targetType,
        SemanticModel model)
    {
        var checkedSymbol = model.GetSymbolInfo(checkedExpr).Symbol;
        var targetTypeSymbol = model.GetTypeInfo(targetType).Type;

        if (checkedSymbol is null || targetTypeSymbol is null)
            return false;

        foreach (var cast in body.DescendantNodes().OfType<CastExpressionSyntax>())
        {
            var castTypeSymbol = model.GetTypeInfo(cast.Type).Type;
            var castExprSymbol = model.GetSymbolInfo(cast.Expression).Symbol;

            if (SymbolEqualityComparer.Default.Equals(castTypeSymbol, targetTypeSymbol) &&
                SymbolEqualityComparer.Default.Equals(castExprSymbol, checkedSymbol))
            {
                return true;
            }
        }

        return false;
    }
}
