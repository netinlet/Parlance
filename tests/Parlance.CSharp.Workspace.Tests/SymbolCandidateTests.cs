using Parlance.CSharp.Workspace;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class SymbolCandidateTests
{
    [Fact]
    public void SymbolCandidate_HasNoDisplayName() =>
        Assert.Null(typeof(SymbolCandidate).GetProperty("DisplayName"));
}
