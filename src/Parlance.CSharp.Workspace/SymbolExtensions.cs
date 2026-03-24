namespace Parlance.CSharp.Workspace;

public static class SymbolExtensions
{
    public static SymbolCandidate ToCandidate(this ResolvedSymbol resolved) =>
        SymbolCandidate.From(resolved);
}
