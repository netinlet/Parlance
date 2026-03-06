using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Analyzers.Rules;

/// <summary>
/// Mirrors IDE0090: suggests converting <c>List&lt;int&gt; x = new List&lt;int&gt;()</c>
/// to <c>List&lt;int&gt; x = new()</c> (target-typed new, C# 9+).
///
/// Only flags when the target type is spatially apparent:
/// - Variable declarations with an explicit (non-var) type
/// - Field / property initializers
///
/// Verifies via semantic model that the constructed type exactly matches
/// the declared type, avoiding false positives on polymorphic assignments.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL9002_UseImplicitObjectCreation : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL9002";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Use implicit object creation",
        messageFormat: "Object creation can be simplified to 'new()'",
        category: "Modernization",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Target-typed new (C# 9+) lets you omit the type name when it is apparent from the declaration, reducing redundancy.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var parseOptions = compilationContext.Compilation.SyntaxTrees
                .FirstOrDefault()?.Options as CSharpParseOptions;

            if (parseOptions?.LanguageVersion < LanguageVersion.CSharp9)
                return;

            compilationContext.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        });
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;

        // Must be inside an EqualsValueClause (covers locals, fields, properties)
        if (objectCreation.Parent is not EqualsValueClauseSyntax equalsValue)
            return;

        var typeNode = GetDeclaredType(equalsValue);
        if (typeNode is null || typeNode.IsVar)
            return;

        var semanticModel = context.SemanticModel;
        var ct = context.CancellationToken;

        var leftType = semanticModel.GetTypeInfo(typeNode, ct).Type;
        var rightType = semanticModel.GetTypeInfo(objectCreation, ct).Type;

        if (leftType is null || rightType is null)
            return;

        if (leftType.TypeKind == TypeKind.Error || rightType.TypeKind == TypeKind.Error)
            return;

        if (!SymbolEqualityComparer.Default.Equals(leftType, rightType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            objectCreation.Type.GetLocation()));
    }

    private static TypeSyntax? GetDeclaredType(EqualsValueClauseSyntax equalsValue)
    {
        // Local variable or field: int x = new int();
        if (equalsValue.Parent is VariableDeclaratorSyntax
            {
                Parent: VariableDeclarationSyntax declaration
            })
        {
            return declaration.Type;
        }

        // Property initializer: public int X { get; } = new int();
        if (equalsValue.Parent is PropertyDeclarationSyntax property)
        {
            return property.Type;
        }

        return null;
    }
}
