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
        var assemblyDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        var referenceNames = new[]
        {
            "System.Runtime.dll",
            "System.Collections.dll",
            "System.Collections.Immutable.dll",
            "System.Linq.dll",
            "System.Console.dll",
            "System.Threading.dll",
            "System.Threading.Tasks.dll",
            "System.ComponentModel.dll",
            "System.ObjectModel.dll",
            "System.Private.CoreLib.dll",
            "netstandard.dll",
        };

        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var name in referenceNames)
        {
            var path = Path.Combine(assemblyDir, name);
            if (File.Exists(path))
                builder.Add(MetadataReference.CreateFromFile(path));
        }

        return builder.ToImmutable();
    }
}
