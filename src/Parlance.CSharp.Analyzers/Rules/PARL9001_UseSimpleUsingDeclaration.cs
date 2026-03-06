using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.CSharp.Analyzers.Rules;

/// <summary>
/// Mirrors IDE0063: suggests converting <c>using (var x = y) { }</c>
/// to <c>using var x = y;</c> (using declarations, C# 8+).
///
/// Only flags when semantics are preserved:
/// - The using must have a variable declaration (not a bare expression).
/// - All nested usings in the chain must also have declarations.
/// - The using must be the last statement in its block, or followed only
///   by <c>return;</c>, <c>break;</c>, or <c>continue;</c>, so disposal
///   timing is unchanged.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL9001_UseSimpleUsingDeclaration : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL9001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Use simple using declaration",
        messageFormat: "using statement can be simplified to a using declaration",
        category: "Modernization",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Using declarations (C# 8+) remove the need for braces and reduce indentation. The variable is disposed at the end of the enclosing scope.");

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

            compilationContext.RegisterSyntaxNodeAction(AnalyzeUsingStatement, SyntaxKind.UsingStatement);
        });
    }

    private static void AnalyzeUsingStatement(SyntaxNodeAnalysisContext context)
    {
        var usingStatement = (UsingStatementSyntax)context.Node;

        // Only flag the outermost using in a nested chain
        if (usingStatement.Parent is UsingStatementSyntax)
            return;

        // Must be directly inside a block (not a switch section, etc.)
        if (usingStatement.Parent is not BlockSyntax block)
            return;

        // Walk the nested using chain — every using must have a declaration
        for (var current = usingStatement; current is not null; current = current.Statement as UsingStatementSyntax)
        {
            if (current.Declaration is null)
                return;
        }

        // Check disposal timing safety: the using must be the last meaningful
        // statement in the block, or followed only by safe exit statements
        if (!IsDisposalTimingSafe(block, usingStatement))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            usingStatement.UsingKeyword.GetLocation()));
    }

    private static bool IsDisposalTimingSafe(BlockSyntax block, UsingStatementSyntax usingStatement)
    {
        var statements = block.Statements;
        var index = statements.IndexOf(usingStatement);

        // Skip any trailing local function declarations — they don't execute inline
        var nextIndex = index + 1;
        while (nextIndex < statements.Count && statements[nextIndex] is LocalFunctionStatementSyntax)
            nextIndex++;

        // Last statement in block — safe
        if (nextIndex >= statements.Count)
            return true;

        // Followed by a bare return, break, or continue — safe
        var nextStatement = statements[nextIndex];
        return nextStatement switch
        {
            ReturnStatementSyntax { Expression: null } => true,
            BreakStatementSyntax => true,
            ContinueStatementSyntax => true,
            _ => false,
        };
    }
}
