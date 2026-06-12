using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Parlance.CSharp.Workspace;

/// <summary>
/// Agent-supplied choices for an option-gated refactoring. All null ⇒ defaults (select-all,
/// Roslyn's default interface name). Member names are matched against candidate <c>ISymbol.Name</c>.
/// </summary>
public sealed record RefactoringOptions(
    ImmutableList<string>? Members = null,
    string? InterfaceName = null,
    bool? NewFile = null,
    ImmutableDictionary<string, bool>? Flags = null);

/// <summary>
/// Request-scoped seam between an MCP request and the MEF-constructed option services (which cannot
/// take Parlance services by DI). <see cref="CodeActionService"/> enters a scope around
/// <c>GetOperationsAsync</c>; the option service reads <see cref="Current"/> and writes any
/// <see cref="Fail"/> message back through a flow-shared box the caller reads via <see cref="CapturedFailure"/>.
/// </summary>
public static class CodeActionOptionsScope
{
    private static readonly AsyncLocal<RefactoringOptions?> _options = new();
    private static readonly AsyncLocal<StrongBox<string?>?> _failure = new();

    public static RefactoringOptions? Current => _options.Value;
    public static string? CapturedFailure => _failure.Value?.Value;

    public static IDisposable Enter(RefactoringOptions? options)
    {
        _options.Value = options;
        _failure.Value = new StrongBox<string?>(null);
        return new Scope();
    }

    public static void Fail(string message)
    {
        if (_failure.Value is { } box)
            box.Value = message;
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            _options.Value = null;
            _failure.Value = null;
        }
    }
}
