using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.ExtractInterface;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;

namespace Parlance.CSharp.Workspace.HostServices;

/// <summary>
/// Headless <see cref="IExtractInterfaceOptionsService"/>, mirroring Microsoft's own
/// <c>LspExtractInterfaceOptionsService</c>. Default: Roslyn's name, all members, same file.
/// Override (via <see cref="CodeActionOptionsScope"/>): substitute name, filter members, new-file toggle.
/// </summary>
[ExportWorkspaceService(typeof(IExtractInterfaceOptionsService)), Shared]
internal sealed class ParlanceExtractInterfaceOptionsService : IExtractInterfaceOptionsService
{
    [ImportingConstructor]
    public ParlanceExtractInterfaceOptionsService() { }

    public ExtractInterfaceOptionsResult GetExtractInterfaceOptions(
        Document document, ImmutableArray<ISymbol> extractableMembers, string defaultInterfaceName,
        ImmutableArray<string> conflictingTypeNames, string defaultNamespace,
        string generatedNameTypeParameterSuffix)
    {
        var opts = CodeActionOptionsScope.Current;
        var name = opts?.InterfaceName ?? defaultInterfaceName;

        var included = extractableMembers;
        if (opts?.Members is { } names && names.Count > 0)
        {
            var byName = extractableMembers.ToLookup(m => m.Name);
            var unknown = names.FirstOrDefault(n => !byName.Contains(n));
            if (unknown is not null)
            {
                CodeActionOptionsScope.Fail(
                    $"member '{unknown}' not found; available: {string.Join(", ", extractableMembers.Select(m => m.Name))}");
                return ExtractInterfaceOptionsResult.Cancelled;
            }
            included = names.SelectMany(n => byName[n]).ToImmutableArray();
        }

        var location = opts?.NewFile == true
            ? ExtractInterfaceOptionsResult.ExtractLocation.NewFile
            : ExtractInterfaceOptionsResult.ExtractLocation.SameFile;

        return new ExtractInterfaceOptionsResult(
            isCancelled: false, includedMembers: included, interfaceName: name,
            fileName: name + ".cs", location: location);
    }
}
