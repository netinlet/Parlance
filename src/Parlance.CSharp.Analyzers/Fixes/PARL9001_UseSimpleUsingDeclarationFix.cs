using System.Collections.Generic;
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
public sealed class PARL9001_UseSimpleUsingDeclarationFix : CodeFixProvider
{
    private const string Title = "Convert to using declaration";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        [PARL9001_UseSimpleUsingDeclaration.DiagnosticId];

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // The diagnostic is reported on the using keyword token.
        // Find the enclosing UsingStatementSyntax.
        var token = root.FindToken(diagnosticSpan.Start);
        var usingStatement = token.Parent as UsingStatementSyntax;
        if (usingStatement is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => ConvertToUsingDeclarationsAsync(context.Document, usingStatement, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ConvertToUsingDeclarationsAsync(
        Document document,
        UsingStatementSyntax outerUsing,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        // Collect the chain of nested using statements
        var usingChain = new List<UsingStatementSyntax>();
        for (UsingStatementSyntax? current = outerUsing; current is not null; current = current.Statement as UsingStatementSyntax)
        {
            usingChain.Add(current);
        }

        // The innermost using's Statement is the block containing the body
        var innermostUsing = usingChain[usingChain.Count - 1];
        var bodyBlock = innermostUsing.Statement as BlockSyntax;
        if (bodyBlock is null)
            return document;

        var newStatements = new List<StatementSyntax>();

        // Convert each using statement to a using declaration
        for (var i = 0; i < usingChain.Count; i++)
        {
            var usingStmt = usingChain[i];
            var declaration = usingStmt.Declaration;
            if (declaration is null)
                return document;

            var localDeclaration = SyntaxFactory.LocalDeclarationStatement(
                    attributeLists: default,
                    awaitKeyword: default,
                    usingKeyword: SyntaxFactory.Token(SyntaxKind.UsingKeyword),
                    modifiers: default,
                    declaration: declaration.WithoutLeadingTrivia(),
                    semicolonToken: SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                .WithAdditionalAnnotations(Formatter.Annotation);

            if (i == 0)
            {
                // Preserve leading trivia from the original outer using statement
                localDeclaration = localDeclaration.WithLeadingTrivia(usingStmt.GetLeadingTrivia());
            }
            else
            {
                // For nested usings, use the leading trivia from the nested using keyword
                localDeclaration = localDeclaration.WithLeadingTrivia(usingStmt.GetLeadingTrivia());
            }

            newStatements.Add(localDeclaration);
        }

        // Add the body statements from the innermost block, preserving trivia.
        // We need to handle the leading trivia of the open brace (comments inside the block)
        // and the body statements themselves.
        var bodyStatements = bodyBlock.Statements;

        if (bodyStatements.Count > 0)
        {
            // Capture any trivia from the open brace that belongs to the first body statement
            var openBraceTrailingTrivia = bodyBlock.OpenBraceToken.TrailingTrivia;
            var firstStatement = bodyStatements[0];

            // The first body statement may need leading trivia adjusted
            // to match the indentation level of the using declarations
            newStatements.Add(firstStatement);

            for (var i = 1; i < bodyStatements.Count; i++)
            {
                newStatements.Add(bodyStatements[i]);
            }
        }

        var newRoot = root.ReplaceNode(
            outerUsing,
            newStatements.Select(s => s.WithAdditionalAnnotations(Formatter.Annotation)));

        return document.WithSyntaxRoot(newRoot);
    }
}
