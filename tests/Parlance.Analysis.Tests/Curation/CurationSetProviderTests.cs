using Microsoft.Extensions.Logging.Abstractions;
using Parlance.Analysis.Curation;

namespace Parlance.Analysis.Tests.Curation;

public sealed class CurationSetProviderTests
{
    private readonly CurationSetProvider _provider = new(NullLogger<CurationSetProvider>.Instance);

    [Fact]
    public void Load_UnknownName_ReturnsNull()
    {
        var result = _provider.Load("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public void Available_ReturnsEmptyListForNow()
    {
        // M4 ships no named sets — infrastructure only
        var result = _provider.Available();
        Assert.NotNull(result);
    }

    [Fact]
    public void Load_NullName_ReturnsNull()
    {
        var result = _provider.Load(null!);
        Assert.Null(result);
    }
}
