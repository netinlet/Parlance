namespace Parlance.CSharp.Workspace;

public static class SymbolExtensions
{
    extension(ResolvedSymbol resolved)
    {
        public SymbolCandidate ToCandidate() => SymbolCandidate.From(resolved);
    }
}
