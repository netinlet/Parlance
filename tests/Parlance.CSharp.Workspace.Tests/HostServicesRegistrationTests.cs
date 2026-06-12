using Microsoft.CodeAnalysis.ExtractInterface;
using Microsoft.CodeAnalysis.PickMembers;
using Parlance.CSharp.Workspace.HostServices;
using Xunit;

namespace Parlance.CSharp.Workspace.Tests;

public sealed class HostServicesRegistrationTests
{
    [Fact]
    public void ComposedHost_ResolvesParlanceOptionServices()
    {
        using var workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(
            CSharpWorkspaceSession.CreateHostServicesForTest());

        Assert.IsType<ParlancePickMembersService>(
            workspace.Services.GetService<IPickMembersService>());
        Assert.IsType<ParlanceExtractInterfaceOptionsService>(
            workspace.Services.GetService<IExtractInterfaceOptionsService>());
    }
}
