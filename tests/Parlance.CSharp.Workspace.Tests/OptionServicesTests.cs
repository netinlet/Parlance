using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.ExtractInterface;
using Microsoft.CodeAnalysis.PickMembers;
using Parlance.CSharp.Workspace;
using Parlance.CSharp.Workspace.HostServices;
using Xunit;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class OptionServicesTests
{
    private static ImmutableArray<ISymbol> Members()
    {
        var comp = CSharpCompilation.Create("t",
            new[] { CSharpSyntaxTree.ParseText(
                "public class C { public int A {get;set;} public int B {get;set;} }") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var type = comp.GetTypeByMetadataName("C")!;
        return type.GetMembers().Where(m => m is IPropertySymbol).ToImmutableArray();
    }

    [Fact]
    public void PickMembers_Default_EchoesAllSelected()
    {
        var svc = new ParlancePickMembersService();
        var result = svc.PickMembers("t", Members());
        Assert.Equal(2, result.Members.Length);
        Assert.True(result.SelectedAll);
        Assert.False(result.IsCanceled);
    }

    [Fact]
    public void PickMembers_Override_FiltersByName()
    {
        var svc = new ParlancePickMembersService();
        using (CodeActionOptionsScope.Enter(new RefactoringOptions(Members: ImmutableList.Create("A"))))
        {
            var result = svc.PickMembers("t", Members());
            Assert.Equal("A", Assert.Single(result.Members).Name);
            Assert.False(result.IsCanceled);
        }
    }

    [Fact]
    public void PickMembers_UnknownMember_CancelsAndCapturesFailure()
    {
        var svc = new ParlancePickMembersService();
        using (CodeActionOptionsScope.Enter(new RefactoringOptions(Members: ImmutableList.Create("Nope"))))
        {
            var result = svc.PickMembers("t", Members());
            Assert.True(result.IsCanceled);
            Assert.Contains("Nope", CodeActionOptionsScope.CapturedFailure);
            Assert.Contains("A, B", CodeActionOptionsScope.CapturedFailure);
        }
    }

    [Fact]
    public void ExtractInterface_Default_UsesRoslynNameAndSameFile()
    {
        var svc = new ParlanceExtractInterfaceOptionsService();
        var result = svc.GetExtractInterfaceOptions(
            document: null!, Members(), "ITarget", ImmutableArray<string>.Empty, "Ns", "");
        Assert.Equal("ITarget", result.InterfaceName);
        Assert.Equal("ITarget.cs", result.FileName);
        Assert.Equal(ExtractInterfaceOptionsResult.ExtractLocation.SameFile, result.Location);
        Assert.False(result.IsCancelled);
    }

    [Fact]
    public void ExtractInterface_Override_SubstitutesNameMembersAndNewFile()
    {
        var svc = new ParlanceExtractInterfaceOptionsService();
        using (CodeActionOptionsScope.Enter(new RefactoringOptions(
            Members: ImmutableList.Create("A"), InterfaceName: "IFoo", NewFile: true)))
        {
            var result = svc.GetExtractInterfaceOptions(
                document: null!, Members(), "ITarget", ImmutableArray<string>.Empty, "Ns", "");
            Assert.Equal("IFoo", result.InterfaceName);
            Assert.Equal("IFoo.cs", result.FileName);
            Assert.Equal("A", Assert.Single(result.IncludedMembers).Name);
            Assert.Equal(ExtractInterfaceOptionsResult.ExtractLocation.NewFile, result.Location);
        }
    }
}
