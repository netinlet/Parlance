using System.Collections.Immutable;
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

    private static ImmutableArray<MetadataReference> LoadReferences()
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
