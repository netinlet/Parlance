using System.Linq;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL0002_PreferCollectionExpressions : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL0002";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Prefer collection expression",
        messageFormat: "Use a collection expression instead of '{0}'",
        category: "Modernization",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Collection expressions (C# 12+) provide a unified, concise syntax for creating collections and let the compiler choose the optimal type.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeArrayCreation, SyntaxKind.ArrayCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        if (creation.Initializer is null)
            return;

        var typeInfo = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return;

        if (!ImplementsIEnumerable(namedType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            creation.GetLocation(),
            creation.Type.ToString()));
    }

    private static void AnalyzeArrayCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ArrayCreationExpressionSyntax)context.Node;

        if (creation.Initializer is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            creation.GetLocation(),
            creation.Type.ToString()));
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        var symbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol;
        if (symbol is not IMethodSymbol method)
            return;

        if (method.Name == "Empty" &&
            method.ContainingType.SpecialType == SpecialType.System_Array &&
            method.IsGenericMethod)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                invocation.GetLocation(),
                "Array.Empty<T>()"));
        }
    }

    private static bool ImplementsIEnumerable(INamedTypeSymbol type)
    {
        return type.AllInterfaces.Any(i =>
            i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
    }
}
