using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Parlance.CSharp.Analyzers.Metrics.Experimental;

// ──────────────────────────────────────────────────────────────────────────────────────────
//  CyclomaticComplexityMetric
//  ===========================
//
//  McCabe's cyclomatic complexity (1976) in plain English
//  ------------------------------------------------------
//  Cyclomatic complexity counts the number of linearly independent paths through a program
//  unit. Intuition: how many distinct decision-combinations does a test suite have to cover
//  to exercise every branch at least once? McCabe gave three equivalent definitions over
//  the control-flow graph G = (N, E, P):
//
//    (1) E − N + 2P      — edges minus nodes plus twice the number of connected
//                          components. For a single-entry/single-exit procedure P = 1 and
//                          this reduces to E − N + 2. This is the formula most textbooks
//                          quote, but it is *not* the one this metric uses (see below).
//
//    (2) π + 1           — the number of predicate nodes plus one. A predicate node is a
//                          basic block with more than one outgoing edge (a conditional
//                          branch). This is the definition Parlance uses; for structured
//                          code it is equal to (1).
//
//    (3) R               — the number of regions in a planar embedding of the CFG. Useful
//                          for theoretical reasoning, not for computation.
//
//  Why π + 1 and not E − N + 2
//  ----------------------------
//  Roslyn's lowered CFG collapses empty-branch bodies. For a sequence of `if (x) { }`
//  statements, both the true and false successors of each conditional lead to the same
//  continuation block, so the graph becomes a straight line of predicate blocks with
//  merged successors. E − N + 2 collapses to 1 under that optimization, which is wrong.
//  Predicate-counting is unaffected: every conditional branch still materializes as a
//  basic block with a non-null `ConditionalSuccessor`, even when its two targets happen
//  to be the same block.
//
//  This was confirmed by the spike in docs/research/2026-04-16-parl3002-analysis.md,
//  using the upstream ReSharper fixture `ManySequentialIfs` (83 empty `if` statements):
//    - E − N + 2      →  1   (wrong — graph collapsed)
//    - π + 1          →  84  (correct — matches ReSharper and CA1502 gold value)
//
//  What is a predicate in C#
//  --------------------------
//  Every source construct that produces a conditional branch in the CFG:
//    - `if` statements
//    - Loop conditions: `for`, `foreach`, `while`, `do`
//    - Short-circuit operators: `&&`, `||`
//    - Ternary conditional: `? :`
//    - `catch` clauses (entry and filter — each `when (...)` is its own predicate)
//    - Pattern `when` guards (in `switch` cases and `switch` expression arms)
//    - `switch` case labels (each non-default label is one predicate)
//    - `switch` expression arms (Roslyn's CFG also models an implicit "no match throws
//      SwitchExpressionException" branch even when a discard arm is present)
//
//  Simple statements, variable declarations, field access, unconditional expressions and
//  `else` branches add no predicates — the `if` already contributed its one predicate.
//
//  Single-path calculation — no fallback
//  -------------------------------------
//  Calculation requires a SemanticModel. When Roslyn cannot produce an IOperation root or
//  ControlFlowGraph.Create declines, we return a Skipped result with a reason. We do NOT
//  fall back to a syntactic decision walker, because that second algorithm produces
//  subtly different numbers than CFG predicate-counting for some shapes (for example, a
//  switch expression with a discard arm: syntax says 3, CFG says 4, for the same code).
//
//  A silent fallback that sometimes produces a different number than the primary path is
//  exactly the kind of path-dependent metric that makes threshold-based diagnostics
//  unreliable: the same method could cross or not cross a threshold depending on which
//  algorithm happened to run. An honest "skip" is better than a plausible-looking wrong
//  answer. See docs/research/2026-04-16-parl3002-analysis.md § "Fallback decision".
//
//  Best-effort on parse errors
//  ---------------------------
//  We do not check `SyntaxTree.GetDiagnostics().Any()` (which is file-scoped and would
//  skip every method in a file with any parse error anywhere). We hand the declaration
//  to Roslyn; if its semantic analysis can produce a usable IOperation we compute the
//  CFG, and if it cannot we skip with a reason. This matches the best-effort philosophy
//  of CognitiveComplexityMetric.
//
//  Gotcha: ControlFlowGraph.Create requires a root operation
//  ---------------------------------------------------------
//  ControlFlowGraph.Create throws ArgumentException if its input operation has a non-null
//  parent. Consequently `Calculate` is invoked with the *whole declaration* (e.g. a
//  MethodDeclarationSyntax), not just its body — `SemanticModel.GetOperation` on the
//  declaration returns an IMethodBodyOperation, which is always a root operation. Passing
//  the body node directly would give an IBlockOperation with a non-null parent, and CFG
//  creation would fail.
// ──────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Result of a cyclomatic complexity calculation.
/// </summary>
/// <param name="Complexity">
/// Number of linearly independent paths through the method, computed as π + 1 (predicate
/// nodes plus one). Only meaningful when <paramref name="SkippedReason"/> is <see langword="null"/>.
/// </param>
/// <param name="EdgeCount">
/// Number of control-flow edges in the CFG.
/// </param>
/// <param name="NodeCount">
/// Number of basic blocks in the CFG, including entry/exit blocks.
/// </param>
/// <param name="SkippedReason">
/// Non-null when the CFG could not be built (e.g. no body, or Roslyn declined to produce
/// a usable IOperation). A null value means the complexity number is meaningful.
/// </param>
internal sealed record CyclomaticComplexityResult(
    int Complexity,
    int EdgeCount,
    int NodeCount,
    string? SkippedReason = null);

/// <summary>
/// Calculates McCabe's cyclomatic complexity for C# methods, local functions, accessors,
/// and expression-bodied members, using Roslyn's control-flow graph and the π + 1
/// predicate-counting formulation. There is no syntactic fallback — when a CFG cannot be
/// built, the result is skipped. See the file header for the underlying theory.
/// </summary>
internal static class CyclomaticComplexityMetric
{
    /// <summary>
    /// Calculates cyclomatic complexity for a method declaration, local function,
    /// accessor, or expression-bodied property/indexer.
    /// </summary>
    /// <param name="declaration">
    /// The whole declaration syntax node (not just the body). CFG construction requires
    /// a root operation, which <see cref="SemanticModel.GetOperation"/> only produces
    /// when invoked on a declaration.
    /// </param>
    /// <param name="semanticModel">
    /// The semantic model for the compilation that contains <paramref name="declaration"/>.
    /// Required — cyclomatic complexity is defined as a property of the control-flow
    /// graph, which requires semantic analysis.
    /// </param>
    public static CyclomaticComplexityResult Calculate(SyntaxNode declaration, SemanticModel semanticModel)
    {
        // A declaration with no body at all — an abstract method, or a property passed in
        // whole when only its accessors have bodies — cannot have a CFG. Return a
        // skipped result (not complexity 0, which would be a meaningful score; the
        // minimum for any executing body is 1).
        if (GetBodyOrExpression(declaration) is null)
            return Skipped("No supported body or expression was available.");

        // Local functions need a different path: Roslyn does not expose a direct
        // ControlFlowGraph.Create overload for ILocalFunctionOperation. Their CFG lives
        // nested inside the containing member's CFG and is retrieved via
        // GetLocalFunctionControlFlowGraph. See CalculateLocalFunction for details.
        if (declaration is LocalFunctionStatementSyntax localFunction)
            return CalculateLocalFunction(localFunction, semanticModel);

        // For expression-bodied properties and indexers, GetOperation(declaration) returns
        // null because the declaration itself has no associated operation. The operation
        // tree lives under the arrow-expression clause. We query that instead.
        var operation = GetRootOperation(declaration, semanticModel);
        if (operation is null)
            return Skipped("Roslyn produced no IOperation for the declaration.");

        ControlFlowGraph? cfg;
        try
        {
            cfg = CreateCfg(operation);
        }
        catch (ArgumentException ex)
        {
            // Defensive: Roslyn's accepted root shapes have shifted across versions. If a
            // future build rejects one we passed in, skip honestly rather than guess.
            return Skipped($"ControlFlowGraph.Create rejected the operation: {ex.Message}");
        }

        if (cfg is null)
            return Skipped($"Operation shape '{operation.GetType().Name}' is not a CFG root.");

        return BuildResultFromCfg(cfg);
    }

    /// <summary>
    /// Resolves the root IOperation for a declaration. For most declarations this is
    /// just <c>GetOperation(declaration)</c>, but expression-bodied properties and
    /// indexers have no operation at the declaration level; their operation is on the
    /// <see cref="ArrowExpressionClauseSyntax"/> child.
    /// </summary>
    private static IOperation? GetRootOperation(SyntaxNode declaration, SemanticModel semanticModel)
    {
        var operation = semanticModel.GetOperation(declaration);
        if (operation is not null)
            return operation;

        // Expression-bodied property / indexer: no operation at the declaration level,
        // but the arrow-clause operation has no parent operation (because the property
        // itself has none) and is therefore a CFG root.
        return declaration switch
        {
            PropertyDeclarationSyntax { ExpressionBody: { } eb } => semanticModel.GetOperation(eb),
            IndexerDeclarationSyntax { ExpressionBody: { } eb } => semanticModel.GetOperation(eb),
            _ => null,
        };
    }

    /// <summary>
    /// Dispatches an <see cref="IOperation"/> to the matching
    /// <see cref="ControlFlowGraph.Create(IMethodBodyOperation, System.Threading.CancellationToken)"/>
    /// overload. Returns <see langword="null"/> for shapes that are not valid CFG roots.
    /// </summary>
    private static ControlFlowGraph? CreateCfg(IOperation operation)
    {
        return operation switch
        {
            // Method/accessor/operator/destructor/lambda bodies, including expression-bodied forms.
            IMethodBodyOperation methodBody => ControlFlowGraph.Create(methodBody),

            // Constructors with explicit `: this(...)` or `: base(...)` initializers are wrapped
            // in IConstructorBodyOperation rather than IMethodBodyOperation. Even constructors
            // without initializers use this shape.
            IConstructorBodyOperation ctorBody => ControlFlowGraph.Create(ctorBody),

            // The arrow-clause of an expression-bodied property/indexer lowers to an
            // IBlockOperation with no parent operation and is therefore a CFG root.
            IBlockOperation block when block.Parent is null => ControlFlowGraph.Create(block),

            _ => null,
        };
    }

    /// <summary>
    /// Computes cyclomatic complexity for a local function. Roslyn has no direct CFG
    /// constructor for <see cref="ILocalFunctionOperation"/>; a local function's CFG lives
    /// as a nested graph inside the containing member's CFG and must be retrieved via
    /// <see cref="ControlFlowGraph.GetLocalFunctionControlFlowGraph"/>. Nested local
    /// functions (local-function-inside-local-function, or inside a lambda) require
    /// recursive descent through the containing graphs.
    /// </summary>
    private static CyclomaticComplexityResult CalculateLocalFunction(LocalFunctionStatementSyntax localFunction, SemanticModel semanticModel)
    {
        var enclosing = FindEnclosingMemberDeclaration(localFunction);
        if (enclosing is null)
            return Skipped("Local function is not inside a member declaration.");

        var enclosingOperation = semanticModel.GetOperation(enclosing);
        if (enclosingOperation is null)
            return Skipped("Enclosing member has no IOperation.");

        ControlFlowGraph? enclosingCfg;
        try
        {
            enclosingCfg = CreateCfg(enclosingOperation);
        }
        catch (ArgumentException ex)
        {
            return Skipped($"Enclosing CFG could not be built: {ex.Message}");
        }

        if (enclosingCfg is null)
            return Skipped($"Enclosing operation shape '{enclosingOperation.GetType().Name}' is not a CFG root.");

        if (semanticModel.GetDeclaredSymbol(localFunction) is not IMethodSymbol symbol)
            return Skipped("Could not resolve local function symbol.");

        var localCfg = FindLocalFunctionCfg(enclosingCfg, symbol);
        if (localCfg is null)
            return Skipped("Local function CFG not found within enclosing graph.");

        return BuildResultFromCfg(localCfg);
    }

    /// <summary>
    /// Walks up the syntax tree to the enclosing member declaration (method, constructor,
    /// accessor, operator, etc.). Stops at the first such ancestor because that is where
    /// Roslyn builds the top-level CFG; nested local functions are reached by recursively
    /// descending through <see cref="FindLocalFunctionCfg"/>.
    /// </summary>
    private static SyntaxNode? FindEnclosingMemberDeclaration(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax)
                return current;
        }
        return null;
    }

    /// <summary>
    /// Recursively searches a CFG (and its nested local-function / anonymous-function
    /// CFGs) for the one belonging to <paramref name="target"/>.
    /// </summary>
    private static ControlFlowGraph? FindLocalFunctionCfg(ControlFlowGraph cfg, IMethodSymbol target)
    {
        foreach (var candidate in cfg.LocalFunctions)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, target))
                return cfg.GetLocalFunctionControlFlowGraph(candidate);

            var nested = cfg.GetLocalFunctionControlFlowGraph(candidate);
            var found = FindLocalFunctionCfg(nested, target);
            if (found is not null)
                return found;
        }

        // A local function can also appear inside a lambda — descend into those too.
        // Walk every IOperation in every block (including BranchValue, which holds the
        // condition for conditional branches and can itself embed lambdas).
        foreach (var block in cfg.Blocks)
        {
            foreach (var op in block.Operations)
            {
                var found = SearchAnonymousFunctions(cfg, op, target);
                if (found is not null)
                    return found;
            }

            if (block.BranchValue is { } branch)
            {
                var found = SearchAnonymousFunctions(cfg, branch, target);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Recursively searches an <see cref="IOperation"/> tree for
    /// <see cref="IFlowAnonymousFunctionOperation"/> children; for each one, resolves its
    /// nested CFG from <paramref name="cfg"/> and recurses into it looking for
    /// <paramref name="target"/>.
    /// </summary>
    private static ControlFlowGraph? SearchAnonymousFunctions(ControlFlowGraph cfg, IOperation operation, IMethodSymbol target)
    {
        if (operation is IFlowAnonymousFunctionOperation anon)
        {
            var anonCfg = cfg.GetAnonymousFunctionControlFlowGraph(anon);
            var found = FindLocalFunctionCfg(anonCfg, target);
            if (found is not null)
                return found;
        }

        foreach (var child in operation.ChildOperations)
        {
            var found = SearchAnonymousFunctions(cfg, child, target);
            if (found is not null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Counts predicate blocks and edges in a CFG and returns the McCabe result.
    /// </summary>
    /// <remarks>
    /// McCabe's π + 1 formulation:
    ///   - Every basic block with a non-null ConditionalSuccessor is a predicate node
    ///     (an `if`, a loop condition, a pattern match, &amp;&amp;/||, a switch dispatch,
    ///     a catch filter, etc.).
    ///   - Blocks that only fall through unconditionally are not predicates.
    ///   - Every outgoing successor is tallied so callers get real EdgeCount/NodeCount
    ///     graph data — though the complexity itself is computed from predicates, not
    ///     from E − N + 2P (see file header for why).
    /// </remarks>
    private static CyclomaticComplexityResult BuildResultFromCfg(ControlFlowGraph cfg)
    {
        var predicateCount = 0;
        var edgeCount = 0;
        var nodeCount = 0;

        foreach (var block in cfg.Blocks)
        {
            nodeCount++;

            if (block.ConditionalSuccessor is not null)
            {
                // A conditional block also has a FallThroughSuccessor — that is the
                // "other" side of the branch, counted unconditionally below.
                predicateCount++;
                edgeCount++;
            }

            if (block.FallThroughSuccessor is not null)
                edgeCount++;
        }

        return new CyclomaticComplexityResult(
            Complexity: predicateCount + 1,
            EdgeCount: edgeCount,
            NodeCount: nodeCount);
    }

    /// <summary>
    /// Extracts the block or expression that holds the body to be scored. Returns
    /// <see langword="null"/> for declarations that have no body we can reach.
    /// </summary>
    private static SyntaxNode? GetBodyOrExpression(SyntaxNode declaration) => declaration switch
    {
        BaseMethodDeclarationSyntax method => method.Body ?? (SyntaxNode?)method.ExpressionBody?.Expression,
        LocalFunctionStatementSyntax localFunction => localFunction.Body ?? (SyntaxNode?)localFunction.ExpressionBody?.Expression,
        AccessorDeclarationSyntax accessor => accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody?.Expression,
        PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
        IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
        _ => null,
    };

    private static CyclomaticComplexityResult Skipped(string reason) => new(Complexity: 0, EdgeCount: 0, NodeCount: 0, SkippedReason: reason);
}
