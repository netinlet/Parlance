using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Parlance.CSharp;

internal static class CompilationFactory
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    public static CSharpCompilation Create(SyntaxTree tree)
    {
        return CSharpCompilation.Create(
            assemblyName: "ParlanceAnalysis",
            syntaxTrees: [tree],
            references: References.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static ImmutableArray<MetadataReference> LoadReferences()
    {
        var refAssemblies = TryLoadRefPackAssemblies();
        if (refAssemblies.Length > 0)
            return refAssemblies;

        return LoadRuntimeAssemblies();
    }

    /// <summary>
    /// Attempt to locate the .NET reference assemblies from the installed SDK target pack.
    /// Reference assemblies are the correct surface for analysis — they contain only public API
    /// surface without implementation details like System.Private.CoreLib.
    /// </summary>
    private static ImmutableArray<MetadataReference> TryLoadRefPackAssemblies()
    {
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        if (string.IsNullOrEmpty(runtimeDir))
            return [];

        // Runtime dir is e.g. /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.0/
        // Ref packs are at    /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.0/ref/net10.0/
        var dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
        var packsRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");

        if (!Directory.Exists(packsRoot))
            return [];

        // Find the highest versioned ref pack
        var latestPack = SelectLatestVersionDirectory(packsRoot);

        if (latestPack is null)
            return [];

        // Look for the ref subdirectory matching the TFM (e.g. ref/net10.0/)
        var refDir = SelectLatestVersionDirectory(Path.Combine(latestPack, "ref"));

        if (refDir is null)
            return [];

        var dlls = Directory.GetFiles(refDir, "*.dll");
        if (dlls.Length == 0)
            return [];

        var builder = ImmutableArray.CreateBuilder<MetadataReference>(dlls.Length);
        foreach (var dll in dlls)
            builder.Add(MetadataReference.CreateFromFile(dll));

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Selects the directory with the highest semantic version name from the given parent directory.
    /// Directories whose names aren't valid versions are ignored.
    /// </summary>
    internal static string? SelectLatestVersionDirectory(string parentDir)
    {
        if (!Directory.Exists(parentDir))
            return null;

        return Directory.GetDirectories(parentDir)
            .Select(d => (Path: d, Name: Path.GetFileName(d)))
            .Where(d => Version.TryParse(d.Name, out _))
            .OrderByDescending(d => Version.Parse(d.Name))
            .Select(d => d.Path)
            .FirstOrDefault();
    }

    /// <summary>
    /// Fallback: load from the host runtime's trusted platform assemblies.
    /// This uses implementation assemblies rather than reference assemblies,
    /// which is less ideal but functional for analysis purposes.
    /// </summary>
    private static ImmutableArray<MetadataReference> LoadRuntimeAssemblies()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(trustedAssemblies))
            return [];

        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Runtime",
            "System.Collections",
            "System.Collections.Immutable",
            "System.Linq",
            "System.Console",
            "System.Threading",
            "System.Threading.Tasks",
            "System.ComponentModel",
            "System.ObjectModel",
            "System.Private.CoreLib",
            "netstandard",
        };

        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(path);
            if (needed.Contains(assemblyName))
                builder.Add(MetadataReference.CreateFromFile(path));
        }

        return builder.ToImmutable();
    }
}
