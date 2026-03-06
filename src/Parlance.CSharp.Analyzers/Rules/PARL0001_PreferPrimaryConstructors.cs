using System.Linq;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL0001_PreferPrimaryConstructors : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL0001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Prefer primary constructor",
        messageFormat: "Type '{0}' can use a primary constructor",
        category: "Modernization",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Primary constructors (C# 12+) combine type declaration and constructor into a single concise form when the constructor only assigns parameters to fields or properties.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var parseOptions = compilationContext.Compilation.SyntaxTrees
                .FirstOrDefault()?.Options as CSharpParseOptions;

            if (parseOptions?.LanguageVersion < LanguageVersion.CSharp12)
                return;

            compilationContext.RegisterSyntaxNodeAction(
                AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration);
        });
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;

        // Skip if already using primary constructor
        if (typeDecl.ParameterList is not null)
            return;

        var constructors = typeDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .ToList();

        // Must have exactly one constructor
        if (constructors.Count != 1)
            return;

        var ctor = constructors[0];

        // Must have parameters
        if (ctor.ParameterList.Parameters.Count == 0)
            return;

        // Must have a body (not expression-bodied)
        if (ctor.Body is null)
            return;

        // Number of assignments must match number of parameters
        if (ctor.Body.Statements.Count != ctor.ParameterList.Parameters.Count)
            return;

        var containingType = context.SemanticModel.GetDeclaredSymbol(typeDecl);
        if (containingType is null)
            return;

        var allPublicSettableProperties = true;

        // Every statement must be a simple assignment to a member of the containing type
        foreach (var statement in ctor.Body.Statements)
        {
            if (!IsSimpleAssignmentToOwnMember(
                    statement, context.SemanticModel, containingType,
                    out var isPublicSettableProperty))
                return;

            if (!isPublicSettableProperty)
                allPublicSettableProperties = false;
        }

        // If ALL assignments are to public settable properties, defer to PARL0003
        // (required properties) to avoid contradictory diagnostics
        if (allPublicSettableProperties)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            typeDecl.Identifier.GetLocation(),
            typeDecl.Identifier.Text));
    }

    private static bool IsSimpleAssignmentToOwnMember(
        StatementSyntax statement,
        SemanticModel model,
        INamedTypeSymbol containingType,
        out bool isPublicSettableProperty)
    {
        isPublicSettableProperty = false;

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

        // RHS must be a parameter reference
        if (assignment.Right is not IdentifierNameSyntax rhsIdentifier)
            return false;

        var rhsSymbol = model.GetSymbolInfo(rhsIdentifier).Symbol;
        if (rhsSymbol is not IParameterSymbol)
            return false;

        // LHS must be a field or property that belongs to the containing type
        var lhsSymbol = model.GetSymbolInfo(assignment.Left).Symbol;

        if (lhsSymbol is IFieldSymbol field)
        {
            if (!SymbolEqualityComparer.Default.Equals(field.ContainingType, containingType))
                return false;

            if (field.IsStatic)
                return false;

            return true;
        }

        if (lhsSymbol is IPropertySymbol property)
        {
            if (!SymbolEqualityComparer.Default.Equals(property.ContainingType, containingType))
                return false;

            if (property.IsStatic)
                return false;

            isPublicSettableProperty =
                property.DeclaredAccessibility == Accessibility.Public &&
                property.SetMethod is { DeclaredAccessibility: Accessibility.Public };

            return true;
        }

        return false;
    }
}
