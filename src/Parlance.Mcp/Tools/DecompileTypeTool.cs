using System.ComponentModel;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class DecompileTypeTool
{
    [McpServerTool(Name = "decompile-type", ReadOnly = true)]
    [Description("Reconstruct full C# source from a NuGet package or external assembly — use when you need to see how an external type is implemented. " +
                 "Does not work on source-defined types; read the source file directly for those. " +
                 "Use a fully qualified type name, e.g., 'Microsoft.CodeAnalysis.Project'.")]
    public static async Task<DecompileTypeResult> DecompileType(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ToolAnalytics analytics, ILogger<DecompileTypeTool> logger,
        string typeName, CancellationToken ct)
    {
        using var _ = analytics.TimeToolCall("decompile-type", new { typeName });

        if (holder.LoadFailure is { } failure)
            return DecompileTypeResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return DecompileTypeResult.NotLoaded();

        await foreach (var (project, compilation) in query.GetCompilationsAsync(ct))
        {
            foreach (var metaRef in compilation.References.OfType<PortableExecutableReference>())
            {
                if (metaRef.FilePath is null) continue;

                var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(metaRef) as IAssemblySymbol;
                if (assemblySymbol is null) continue;

                var typeSymbol = FindTypeInAssembly(assemblySymbol, typeName);
                if (typeSymbol is null) continue;

                try
                {
                    var decompiler = new CSharpDecompiler(
                        metaRef.FilePath,
                        new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });

                    var fullTypeName = new FullTypeName(typeSymbol.ToDisplayString());
                    var decompiledCode = decompiler.DecompileTypeAsString(fullTypeName);

                    const int maxLines = 500;
                    var lines = decompiledCode.Split('\n');
                    var truncated = lines.Length > maxLines;
                    if (truncated)
                        decompiledCode = string.Join('\n', lines[..maxLines]);

                    return DecompileTypeResult.Found(
                        typeSymbol.ToDisplayString(), assemblySymbol.Name, metaRef.FilePath, decompiledCode,
                        truncated ? $"Output truncated to {maxLines} of {lines.Length} lines" : null);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to decompile {Type} from {Assembly}", typeName, metaRef.FilePath);
                    return DecompileTypeResult.DecompilationFailed(typeName, ex.Message);
                }
            }
        }

        return DecompileTypeResult.NotFound(typeName);
    }

    private static INamedTypeSymbol? FindTypeInAssembly(IAssemblySymbol assembly, string typeName)
    {
        var parts = typeName.Split('.');
        INamespaceOrTypeSymbol current = assembly.GlobalNamespace;

        foreach (var part in parts[..^1])
        {
            if (current.GetMembers(part).OfType<INamespaceOrTypeSymbol>().FirstOrDefault() is not { } next)
                return null;
            current = next;
        }

        return current.GetMembers(parts[^1]).OfType<INamedTypeSymbol>().FirstOrDefault();
    }
}

public sealed record DecompileTypeResult(
    string Status, string? TypeName, string? AssemblyName, string? AssemblyPath,
    string? DecompiledSource, string? Message)
{
    public static DecompileTypeResult NotFound(string typeName) => new(
        "not_found", typeName, null, null, null, $"Type '{typeName}' not found in any metadata reference");
    public static DecompileTypeResult NotLoaded() => new(
        "not_loaded", null, null, null, null, "Workspace is still loading");
    public static DecompileTypeResult LoadFailed(string message) => new(
        "load_failed", null, null, null, null, message);
    public static DecompileTypeResult DecompilationFailed(string typeName, string error) => new(
        "decompile_failed", typeName, null, null, null, $"Decompilation failed: {error}");
    public static DecompileTypeResult Found(
        string typeName, string assemblyName, string assemblyPath,
        string decompiledSource, string? message) => new(
        "found", typeName, assemblyName, assemblyPath, decompiledSource, message);
}
