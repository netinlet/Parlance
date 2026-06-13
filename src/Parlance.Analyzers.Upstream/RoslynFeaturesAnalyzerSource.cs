using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;

namespace Parlance.Analyzers.Upstream;

/// <summary>
/// First-party source supplying Roslyn's built-in IDE code fixes and refactorings from
/// <c>Microsoft.CodeAnalysis.CSharp.Features</c> / <c>Microsoft.CodeAnalysis.Features</c>.
/// Contributes no diagnostic analyzers. Loaded reflectively (those assemblies are not
/// referenced by this project) and cached for the process lifetime.
/// </summary>
public sealed class RoslynFeaturesAnalyzerSource : IAnalyzerSource
{
    private static readonly string[] FeatureAssemblies =
        ["Microsoft.CodeAnalysis.CSharp.Features", "Microsoft.CodeAnalysis.Features"];

    private readonly Lazy<SourceLoadResult> _cached = new(LoadFeatures);

    public string Name => "roslyn-features";
    public SourceTrust Trust => SourceTrust.FirstParty;
    public int Priority => 10;

    public SourceLoadResult Load(string targetFramework, string repoPath) => _cached.Value;

    public ImmutableArray<string> Probe(string repoPath) => [];

    private static SourceLoadResult LoadFeatures()
    {
        var fixes = ImmutableArray.CreateBuilder<CodeFixProvider>();
        var refactorings = ImmutableArray.CreateBuilder<CodeRefactoringProvider>();

        foreach (var assemblyName in FeatureAssemblies)
        {
            fixes.AddRange(Discover<CodeFixProvider>(assemblyName));
            refactorings.AddRange(Discover<CodeRefactoringProvider>(assemblyName));
        }

        return new SourceLoadResult(
            new AnalyzerComponents([], fixes.ToImmutable(), refactorings.ToImmutable()),
            []);
    }

    private static IEnumerable<T> Discover<T>(string assemblyName) where T : class
    {
        Assembly assembly;
        try { assembly = Assembly.Load(assemblyName); }
        catch { yield break; }

        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
        catch { yield break; }

        foreach (var type in types)
        {
            if (type.IsAbstract || !typeof(T).IsAssignableFrom(type)) continue;
            T? instance = null;
            try { instance = Activator.CreateInstance(type) as T; }
            catch { /* skip types that can't be instantiated */ }
            if (instance is not null) yield return instance;
        }
    }
}
