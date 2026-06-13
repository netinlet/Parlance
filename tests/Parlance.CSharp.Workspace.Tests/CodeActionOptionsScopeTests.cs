using System.Collections.Immutable;
using Parlance.CSharp.Workspace;
using Xunit;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class CodeActionOptionsScopeTests
{
    [Fact]
    public void Current_IsNull_OutsideScope() =>
        Assert.Null(CodeActionOptionsScope.Current);

    [Fact]
    public async Task Current_SurvivesAwait_AndClearsOnDispose()
    {
        var opts = new RefactoringOptions(Members: ImmutableList.Create("UnitPrice"));
        using (CodeActionOptionsScope.Enter(opts))
        {
            await Task.Yield();
            Assert.Same(opts, CodeActionOptionsScope.Current);
        }
        Assert.Null(CodeActionOptionsScope.Current);
    }

    [Fact]
    public void Fail_IsVisibleToCaller_WithinScope()
    {
        using (CodeActionOptionsScope.Enter(new RefactoringOptions()))
        {
            CodeActionOptionsScope.Fail("boom");
            Assert.Equal("boom", CodeActionOptionsScope.CapturedFailure);
        }
    }
}
