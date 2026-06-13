namespace Parlance.Analysis.Tests.Fixtures;

// Deliberately var-heavy sample governed by the sibling `.editorconfig` (root = true, prefer explicit type).
// Under that pinned style IDE0008 ("use explicit type instead of 'var'") fires on every local below, giving
// the document fix-all integration tests a stable multi-occurrence, FixAll-capable rule that does not depend
// on the repo's own (prefer-var) style. Keep several `var` locals so the fix-all has more than one occurrence
// to collapse.
internal sealed class VarHeavySample
{
    public int Compute()
    {
        var a = 1;
        var b = 2;
        var c = a + b;
        var d = c * a;
        var e = d - b;
        return e;
    }
}
