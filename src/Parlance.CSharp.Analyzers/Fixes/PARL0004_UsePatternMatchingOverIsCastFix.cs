using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Parlance.CSharp.Analyzers.Rules;

namespace Parlance.CSharp.Analyzers.Fixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class PARL0004_UsePatternMatchingOverIsCastFix : CodeFixProvider
{
    private const string Title = "Use pattern matching";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        [PARL0004_UsePatternMatchingOverIsCast.DiagnosticId];

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindNode(diagnosticSpan);
        if (node is not BinaryExpressionSyntax isExpression)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => ApplyPatternMatchingAsync(context.Document, isExpression, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ApplyPatternMatchingAsync(
        Document document,
        BinaryExpressionSyntax isExpression,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
            return document;

        var checkedExpr = isExpression.Left;
        var targetType = isExpression.Right as TypeSyntax;
        if (targetType is null)
            return document;

        // Find the parent if statement
        if (isExpression.Parent is not IfStatementSyntax ifStatement)
            return document;

        var body = ifStatement.Statement as BlockSyntax;
        if (body is null)
            return document;

        // Find the cast variable declaration in the if-body
        var checkedSymbol = semanticModel.GetSymbolInfo(checkedExpr, cancellationToken).Symbol;
        var targetTypeSymbol = semanticModel.GetTypeInfo(targetType, cancellationToken).Type;
        if (checkedSymbol is null || targetTypeSymbol is null)
            return document;

        LocalDeclarationStatementSyntax? castDeclaration = null;
        string? variableName = null;

        foreach (var statement in body.Statements)
        {
            if (statement is not LocalDeclarationStatementSyntax localDecl)
                continue;

            foreach (var variable in localDecl.Declaration.Variables)
            {
                if (variable.Initializer?.Value is not CastExpressionSyntax cast)
                    continue;

                var castTypeSymbol = semanticModel.GetTypeInfo(cast.Type, cancellationToken).Type;
                var castExprSymbol = semanticModel.GetSymbolInfo(cast.Expression, cancellationToken).Symbol;

                if (SymbolEqualityComparer.Default.Equals(castTypeSymbol, targetTypeSymbol) &&
                    SymbolEqualityComparer.Default.Equals(castExprSymbol, checkedSymbol))
                {
                    castDeclaration = localDecl;
                    variableName = variable.Identifier.ValueText;
                    break;
                }
            }

            if (castDeclaration is not null)
                break;
        }

        if (castDeclaration is null || variableName is null)
            return document;

        // Build the new is-pattern expression: expr is Type name
        var designation = SyntaxFactory.SingleVariableDesignation(
            SyntaxFactory.Identifier(variableName));

        var declarationPattern = SyntaxFactory.DeclarationPattern(
            targetType.WithTrailingTrivia(SyntaxFactory.Space),
            designation);

        var isPatternExpression = SyntaxFactory.IsPatternExpression(
            checkedExpr,
            SyntaxFactory.Token(SyntaxKind.IsKeyword)
                .WithLeadingTrivia(SyntaxFactory.Space)
                .WithTrailingTrivia(SyntaxFactory.Space),
            declarationPattern);

        // Remove the cast declaration and build new body
        var newStatements = body.Statements.Remove(castDeclaration);

        // Transfer any leading trivia from the cast declaration to the next statement
        // (but skip the whitespace-only trivia that's just indentation)
        var castLeadingTrivia = castDeclaration.GetLeadingTrivia();
        var castIndex = body.Statements.IndexOf(castDeclaration);
        if (castIndex < body.Statements.Count - 1 && castLeadingTrivia.Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)))
        {
            var nextStatement = newStatements[castIndex];
            var merged = castLeadingTrivia.AddRange(nextStatement.GetLeadingTrivia());
            newStatements = newStatements.Replace(nextStatement, nextStatement.WithLeadingTrivia(merged));
        }

        var newBody = body.WithStatements(newStatements);

        // Build new if statement with pattern matching condition
        var newIfStatement = ifStatement
            .WithCondition(isPatternExpression)
            .WithStatement(newBody)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot = root.ReplaceNode(ifStatement, newIfStatement);
        return document.WithSyntaxRoot(newRoot);
    }
}
