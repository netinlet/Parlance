using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Parlance.CSharp.Analyzers.Metrics;

/// <summary>
/// A single contribution to a cognitive-complexity score: the source location
/// of the construct that triggered the increment, plus a human-readable
/// reason string for that increment. Collected in visit order so that a
/// consumer (tests, MCP/AI, IDE hover) can explain <em>why</em> a method
/// crossed the threshold, not just <em>how far</em>.
/// </summary>
internal sealed record ComplexityIncrement(Location Location, string Reason);

/// <summary>
/// Result of a cognitive-complexity calculation. <see cref="Score"/> is the
/// aggregate number; <see cref="Increments"/> lists the per-site
/// contributions in walk order. The sum of the per-increment weights parsed
/// from the reason strings equals <see cref="Score"/>, but callers should
/// trust <see cref="Score"/> as the authoritative number.
/// </summary>
internal sealed record CognitiveComplexityResult(
    int Score,
    ImmutableList<ComplexityIncrement> Increments);

/// <summary>
/// Calculates cognitive complexity for C# methods, local functions,
/// accessors, constructors, operators, and expression-bodied members using a
/// Roslyn <see cref="CSharpSyntaxWalker"/>. The scoring rules follow the
/// Sonar white paper and the JetBrains Cognitive Complexity plugin, with
/// Parlance-specific divergences documented in <c>docs/rules/PARL3001.md</c>.
/// Internal because this assembly ships only an analyzer surface; consumers
/// access complexity through diagnostics, not through a direct metric API.
/// </summary>
internal static class CognitiveComplexityMetric
{
    /// <summary>
    /// Computes the cognitive complexity of <paramref name="bodyOrExpression"/>
    /// and returns both the aggregate score and the per-site increment list.
    /// </summary>
    /// <param name="bodyOrExpression">
    /// The body block or expression to score. Pass the body of a method, a
    /// local function, an accessor, or the arrow-expression of an
    /// expression-bodied member — not the whole declaration.
    /// </param>
    /// <param name="semanticModel">
    /// Optional semantic model used for recursion detection. When
    /// <see langword="null"/> the walker skips the semantic recursion check.
    /// </param>
    /// <param name="currentMethod">
    /// Optional symbol for the declaration being scored. Required for the
    /// recursion check alongside <paramref name="semanticModel"/>.
    /// </param>
    public static CognitiveComplexityResult Calculate(
        SyntaxNode bodyOrExpression,
        SemanticModel? semanticModel = null,
        IMethodSymbol? currentMethod = null)
    {
        var walker = new Walker(semanticModel, currentMethod);
        walker.Visit(bodyOrExpression);
        return new CognitiveComplexityResult(walker.Score, walker.BuildIncrements());
    }

    private sealed class Walker : CSharpSyntaxWalker
    {
        private readonly SemanticModel? _semanticModel;
        private readonly IMethodSymbol? _currentMethod;
        private readonly List<ComplexityIncrement> _increments = new();
        private int _nesting;

        public Walker(SemanticModel? semanticModel, IMethodSymbol? currentMethod)
            : base(SyntaxWalkerDepth.Node)
        {
            _semanticModel = semanticModel;
            _currentMethod = currentMethod;
        }

        public int Score { get; private set; }

        public ImmutableList<ComplexityIncrement> BuildIncrements()
            => _increments.ToImmutableList();

        // ─── Increment emission helpers ──────────────────────────────────
        // Every Score += site in this walker goes through one of these
        // helpers so the (amount, reason, location) triple is always set
        // together. The reason strings follow Sonar's S3776 vocabulary so
        // MCP / AI consumers see a stable, parseable explanation.

        private void RecordNestingIncrement(SyntaxToken token)
        {
            var amount = 1 + _nesting;
            Score += amount;
            var reason = amount == 1
                ? "+1"
                : string.Format(CultureInfo.InvariantCulture, "+{0} (incl {1} for nesting)", amount, amount - 1);
            _increments.Add(new ComplexityIncrement(token.GetLocation(), reason));
        }

        private void RecordFlatIncrement(SyntaxToken token, int amount, string reason)
        {
            if (amount == 0)
                return;

            Score += amount;
            _increments.Add(new ComplexityIncrement(token.GetLocation(), reason));
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            var isElseIf = IsElseIf(node);
            if (isElseIf)
                RecordFlatIncrement(node.IfKeyword, 1, "+1");
            else
                RecordNestingIncrement(node.IfKeyword);

            Visit(node.Condition);
            VisitNested(node.Statement);

            if (node.Else is null)
                return;

            if (node.Else.Statement is IfStatementSyntax elseIf)
            {
                Visit(elseIf);
                return;
            }

            RecordFlatIncrement(node.Else.ElseKeyword, 1, "+1");
            VisitNested(node.Else.Statement);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            RecordNestingIncrement(node.ForKeyword);

            Visit(node.Declaration);
            foreach (var initializer in node.Initializers)
                Visit(initializer);
            Visit(node.Condition);
            foreach (var incrementor in node.Incrementors)
                Visit(incrementor);

            VisitNested(node.Statement);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            RecordNestingIncrement(node.ForEachKeyword);

            Visit(node.Expression);
            VisitNested(node.Statement);
        }

        public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            RecordNestingIncrement(node.ForEachKeyword);

            Visit(node.Expression);
            VisitNested(node.Statement);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            RecordNestingIncrement(node.WhileKeyword);

            Visit(node.Condition);
            VisitNested(node.Statement);
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            RecordNestingIncrement(node.DoKeyword);

            VisitNested(node.Statement);
            Visit(node.Condition);
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            RecordNestingIncrement(node.SwitchKeyword);

            Visit(node.Expression);
            _nesting++;
            foreach (var section in node.Sections)
            {
                // Labels must be visited so that pattern `when` guards on case
                // labels (`case X when cond:`) contribute their logical operators
                // to the score — otherwise the guard is silently ignored. This
                // mirrors VisitSwitchExpression visiting each arm's WhenClause.
                foreach (var label in section.Labels)
                    Visit(label);

                foreach (var statement in section.Statements)
                    Visit(statement);
            }

            _nesting--;
        }

        public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            RecordNestingIncrement(node.SwitchKeyword);

            Visit(node.GoverningExpression);
            _nesting++;
            foreach (var arm in node.Arms)
            {
                Visit(arm.Pattern);
                Visit(arm.WhenClause);
                Visit(arm.Expression);
            }

            _nesting--;
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            RecordNestingIncrement(node.QuestionToken);

            Visit(node.Condition);
            _nesting++;
            Visit(node.WhenTrue);
            Visit(node.WhenFalse);
            _nesting--;
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            RecordNestingIncrement(node.CatchKeyword);

            if (node.Filter is not null)
            {
                RecordFlatIncrement(node.Filter.WhenKeyword, 1, "+1");
                Visit(node.Filter);
            }

            VisitNested(node.Block);
        }

        public override void VisitBreakStatement(BreakStatementSyntax node)
        {
            // The +1 vs 0 tradeoff and the rationale for our choice live on
            // ComplexityDefaults.BreakIncrement — flip the constant there to
            // change behavior, don't touch this site.
            RecordFlatIncrement(node.BreakKeyword, ComplexityDefaults.BreakIncrement, "+1");
            base.VisitBreakStatement(node);
        }

        public override void VisitGotoStatement(GotoStatementSyntax node)
        {
            // `goto` (including `goto case` and `goto default`) is a hybrid
            // nesting+1 increment — deep jumps are meaningfully harder to
            // follow than shallow ones, so the reason string surfaces the
            // nesting penalty. `break` stays flat (+1) because it only exits
            // the immediately enclosing construct, and `continue` stays 0.
            RecordNestingIncrement(node.GotoKeyword);
            base.VisitGotoStatement(node);
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            VisitNested(node.Body);
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            VisitNested(node.Body);
        }

        public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            VisitNested(node.Body);
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            // Static local functions are scored independently — PARL3001
            // registers them as their own analysis target and they get their
            // own threshold. Folding them into the parent would double-count
            // code that the user already explicitly isolated from its
            // enclosing scope. Non-static locals stay folded at nesting+1
            // because they share the parent's captured state and are
            // conceptually part of its body.
            if (node.Modifiers.Any(SyntaxKind.StaticKeyword))
                return;

            var bodyOrExpression = node.Body ?? (SyntaxNode?)node.ExpressionBody?.Expression;
            if (bodyOrExpression is not null)
                VisitNested(bodyOrExpression);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (IsLogicalExpression(node) && !IsLogicalExpression(node.Parent))
                EmitLogicalOperatorGroups(node);

            base.VisitBinaryExpression(node);
        }

        public override void VisitBinaryPattern(BinaryPatternSyntax node)
        {
            // Score binary patterns (`and` / `or`) with the same group logic as
            // the boolean operators: only emit at the root of a binary-pattern
            // tree, and transition between `and` and `or` starts a new group.
            if (!IsBinaryPattern(node.Parent))
                EmitBinaryPatternGroups(node);

            base.VisitBinaryPattern(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (IsRecursiveCall(node))
                RecordFlatIncrement(GetInvocationToken(node), ComplexityDefaults.RecursionIncrement, "+1 (recursion)");

            base.VisitInvocationExpression(node);
        }

        private static SyntaxToken GetInvocationToken(InvocationExpressionSyntax node)
        {
            // Prefer the called name for recursion readability; fall back to
            // the whole invocation when the expression is something exotic
            // (a delegate invocation, an indexer-returned callable, etc.).
            return node.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier,
                MemberAccessExpressionSyntax mae => mae.Name.Identifier,
                _ => node.GetFirstToken(),
            };
        }

        private void VisitNested(SyntaxNode? node)
        {
            if (node is null)
                return;

            _nesting++;
            Visit(node);
            _nesting--;
        }

        private bool IsRecursiveCall(InvocationExpressionSyntax node)
        {
            if (_semanticModel is null || _currentMethod is null)
                return false;

            var symbol = _semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (symbol is null)
                return false;

            return SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, _currentMethod.OriginalDefinition);
        }

        private static bool IsElseIf(IfStatementSyntax node)
        {
            return node.Parent is ElseClauseSyntax;
        }

        /// <summary>
        /// Emits one +1 increment per new logical-operator group under
        /// <paramref name="root"/>. A "group" is a maximal run of the same
        /// operator kind (<c>&amp;&amp;</c> or <c>||</c>); each transition
        /// between kinds starts a new group. The increment is anchored on the
        /// operator token that begins the group so consumers see the spot
        /// where the reader has to re-evaluate the boolean structure.
        /// </summary>
        private void EmitLogicalOperatorGroups(BinaryExpressionSyntax root)
        {
            SyntaxKind? previousKind = null;
            foreach (var (kind, operatorToken) in GetLogicalOperators(root))
            {
                if (kind != previousKind)
                    RecordFlatIncrement(operatorToken, ComplexityDefaults.NewLogicalGroupIncrement, "+1");
                previousKind = kind;
            }
        }

        private static IEnumerable<(SyntaxKind Kind, SyntaxToken Operator)> GetLogicalOperators(ExpressionSyntax expression)
        {
            if (expression is BinaryExpressionSyntax binary && IsLogicalExpression(binary))
            {
                foreach (var entry in GetLogicalOperators(binary.Left))
                    yield return entry;

                yield return (binary.Kind(), binary.OperatorToken);

                foreach (var entry in GetLogicalOperators(binary.Right))
                    yield return entry;
            }
        }

        private static bool IsLogicalExpression(SyntaxNode? node)
        {
            return node?.IsKind(SyntaxKind.LogicalAndExpression) == true
                || node?.IsKind(SyntaxKind.LogicalOrExpression) == true;
        }

        /// <summary>
        /// Pattern-side analogue of <see cref="EmitLogicalOperatorGroups"/>.
        /// Each maximal run of identical pattern operators (<c>and</c> or
        /// <c>or</c>) counts as one group; the operator token that starts the
        /// group carries the +1. Mixed patterns such as
        /// <c>> 0 and &lt; 10 or > 100</c> therefore score the same way a
        /// boolean expression with the same shape would.
        /// </summary>
        private void EmitBinaryPatternGroups(BinaryPatternSyntax root)
        {
            SyntaxKind? previousKind = null;
            foreach (var (kind, operatorToken) in GetPatternOperators(root))
            {
                if (kind != previousKind)
                    RecordFlatIncrement(operatorToken, ComplexityDefaults.NewLogicalGroupIncrement, "+1");
                previousKind = kind;
            }
        }

        private static IEnumerable<(SyntaxKind Kind, SyntaxToken Operator)> GetPatternOperators(PatternSyntax pattern)
        {
            if (pattern is BinaryPatternSyntax binary && IsBinaryPattern(binary))
            {
                foreach (var entry in GetPatternOperators(binary.Left))
                    yield return entry;

                yield return (binary.Kind(), binary.OperatorToken);

                foreach (var entry in GetPatternOperators(binary.Right))
                    yield return entry;
            }
        }

        private static bool IsBinaryPattern(SyntaxNode? node)
        {
            return node?.IsKind(SyntaxKind.AndPattern) == true
                || node?.IsKind(SyntaxKind.OrPattern) == true;
        }
    }
}
