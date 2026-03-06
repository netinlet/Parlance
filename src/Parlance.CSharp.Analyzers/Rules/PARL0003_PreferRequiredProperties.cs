using System.Linq;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL0003_PreferRequiredProperties : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL0003";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Prefer required properties",
        messageFormat: "Type '{0}' can use required properties instead of constructor initialization",
        category: "Modernization",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The 'required' modifier (C# 11+) enforces that callers set a property during initialization. This is clearer than constructor-only initialization for simple DTOs.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;

        var constructors = typeDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .ToList();

        if (constructors.Count != 1)
            return;

        var ctor = constructors[0];
        if (ctor.ParameterList.Parameters.Count == 0 || ctor.Body is null)
            return;

        if (ctor.Body.Statements.Count != ctor.ParameterList.Parameters.Count)
            return;

        foreach (var statement in ctor.Body.Statements)
        {
            if (!IsAssignmentToPublicSettableProperty(statement, context.SemanticModel))
                return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            typeDecl.Identifier.GetLocation(),
            typeDecl.Identifier.Text));
    }

    private static bool IsAssignmentToPublicSettableProperty(
        StatementSyntax statement,
        SemanticModel model)
    {
        if (statement is not ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression
                } assignment
            })
        {
            return false;
        }

        if (assignment.Right is not IdentifierNameSyntax rhsId)
            return false;

        var rhsSymbol = model.GetSymbolInfo(rhsId).Symbol;
        if (rhsSymbol is not IParameterSymbol)
            return false;

        var lhsSymbol = model.GetSymbolInfo(assignment.Left).Symbol;
        if (lhsSymbol is not IPropertySymbol property)
            return false;

        if (property.DeclaredAccessibility != Accessibility.Public)
            return false;

        if (property.SetMethod is null)
            return false;

        if (property.SetMethod.DeclaredAccessibility != Accessibility.Public)
            return false;

        return true;
    }
}
