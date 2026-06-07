using Parlance.Abstractions;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class RepoPathFieldTests
{
    [Fact]
    public void AnalyzeDiagnostic_FileIsRepoPath() =>
        Assert.Equal(typeof(RepoPath), typeof(AnalyzeDiagnostic).GetProperty("File")!.PropertyType);

    [Fact]
    public void SymbolMatch_FilePathIsNullableRepoPath() =>
        Assert.Equal(typeof(RepoPath?), typeof(SymbolMatch).GetProperty("FilePath")!.PropertyType);

    [Fact]
    public void DefinitionLocation_FilePathIsRepoPath() =>
        Assert.Equal(typeof(RepoPath), typeof(DefinitionLocation).GetProperty("FilePath")!.PropertyType);
}
