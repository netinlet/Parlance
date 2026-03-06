using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Analyzers.Rules;

/// <summary>
/// Mirrors IDE0034: suggests converting <c>default(T)</c> to <c>default</c>
/// when the type can be inferred from context (C# 7.1+).
///
/// Only flags when removing the type argument is safe:
/// - Not inside a <c>var</c> declaration (type would be lost).
/// - Not in an overloaded method argument where removing the type
///   could change overload resolution.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL9003_UseDefaultLiteral : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL9003";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Use default literal",
        messageFormat: "'default({0})' can be simplified to 'default'",
        category: "Modernization",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The default literal (C# 7.1+) lets the compiler infer the type from context, reducing redundancy when the type is already apparent.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var parseOptions = compilationContext.Compilation.SyntaxTrees
                .FirstOrDefault()?.Options as CSharpParseOptions;

            if (parseOptions?.LanguageVersion < LanguageVersion.CSharp7_1)
                return;

            compilationContext.RegisterSyntaxNodeAction(AnalyzeDefaultExpression, SyntaxKind.DefaultExpression);
        });
    }

    private static void AnalyzeDefaultExpression(SyntaxNodeAnalysisContext context)
    {
        var defaultExpression = (DefaultExpressionSyntax)context.Node;

        // Check if this is inside a var declaration — can't simplify
        if (IsInsideVarDeclaration(defaultExpression))
            return;

        // Check if removing the type would change overload resolution
        if (IsAmbiguousOverloadArgument(defaultExpression, context.SemanticModel))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            defaultExpression.GetLocation(),
            defaultExpression.Type));
    }

    private static bool IsInsideVarDeclaration(DefaultExpressionSyntax defaultExpression)
    {
        // Walk up: default(T) → EqualsValueClause → VariableDeclarator → VariableDeclaration
        if (defaultExpression.Parent is EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax declaration
                }
            })
        {
            return declaration.Type.IsVar;
        }

        return false;
    }

    private static bool IsAmbiguousOverloadArgument(
        DefaultExpressionSyntax defaultExpression,
        SemanticModel model)
    {
        // Check if we're a direct argument to a method invocation
        if (defaultExpression.Parent is not ArgumentSyntax
            {
                Parent: ArgumentListSyntax
                {
                    Parent: InvocationExpressionSyntax invocation
                }
            })
        {
            return false;
        }

        // Get the method group — if there are multiple candidates, removing
        // the type from default could change which overload is selected
        var symbolInfo = model.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is IMethodSymbol && symbolInfo.CandidateSymbols.Length == 0)
        {
            // Unique resolution — but check if the containing type has
            // other overloads with the same name and arity
            var method = (IMethodSymbol)symbolInfo.Symbol;
            var overloads = method.ContainingType
                .GetMembers(method.Name)
                .OfType<IMethodSymbol>()
                .Where(m => m.Parameters.Length == method.Parameters.Length)
                .ToList();

            if (overloads.Count > 1)
                return true;
        }

        // If resolution already failed or was ambiguous, don't flag
        if (symbolInfo.Symbol is null)
            return true;

        return false;
    }
}
