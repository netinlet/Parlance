using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.PickMembers;

namespace Parlance.CSharp.Workspace.HostServices;

/// <summary>
/// Headless <see cref="IPickMembersService"/> — the same role Microsoft's own OmniSharp/LSP services
/// play. Default: echo every candidate (select-all). Override (via <see cref="CodeActionOptionsScope"/>):
/// keep only the named members and apply flag values by option id.
/// </summary>
[ExportWorkspaceService(typeof(IPickMembersService), ServiceLayer.Host), Shared]
internal sealed class ParlancePickMembersService : IPickMembersService
{
    [ImportingConstructor]
    public ParlancePickMembersService() { }

    public PickMembersResult PickMembers(
        string title, ImmutableArray<ISymbol> members,
        ImmutableArray<PickMembersOption> options = default, bool selectAll = true)
    {
        options = options.IsDefault ? ImmutableArray<PickMembersOption>.Empty : options;
        var opts = CodeActionOptionsScope.Current;
        if (opts is null || (opts.Members is null && opts.Flags is null))
            return new PickMembersResult(members, options, selectAll);

        var selected = members;
        if (opts.Members is { } names)
        {
            if (names.Count == 0)
            {
                CodeActionOptionsScope.Fail("no members selected; omit 'members' to include all");
                return PickMembersResult.Canceled;
            }

            var byName = members.ToLookup(m => m.Name);
            var unknown = names.FirstOrDefault(n => !byName.Contains(n));
            if (unknown is not null)
            {
                CodeActionOptionsScope.Fail(
                    $"member '{unknown}' not found; available: {string.Join(", ", members.Select(m => m.Name))}");
                return PickMembersResult.Canceled;
            }

            // Filter the candidate list (not the requested names) so the result keeps declaration
            // order and never duplicates a member when a name is requested twice or is overloaded.
            var requested = names.ToHashSet();
            selected = members.Where(m => requested.Contains(m.Name)).ToImmutableArray();
        }

        if (opts.Flags is { } flags)
            foreach (var option in options)
                if (flags.TryGetValue(option.Id, out var value))
                    option.Value = value;

        // Only claim select-all when nothing was filtered out; a strict subset must not report SelectedAll.
        return new PickMembersResult(selected, options, selected.Length == members.Length);
    }
}
