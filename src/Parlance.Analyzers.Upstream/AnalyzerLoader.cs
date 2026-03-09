using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.Analyzers.Upstream;

internal static class AnalyzerLoader
{
    private static readonly HashSet<string> SupportedFrameworks = ["net8.0", "net10.0"];

    public static ImmutableArray<DiagnosticAnalyzer> LoadAll(string targetFramework)
    {
        if (!SupportedFrameworks.Contains(targetFramework))
        {
            throw new ArgumentException(
                $"Unsupported target framework '{targetFramework}'. Supported: {string.Join(", ", SupportedFrameworks)}",
                nameof(targetFramework));
        }

        var analyzers = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();

        // Discover PARL analyzers from the Parlance.CSharp.Analyzers assembly
        var parlAssembly = typeof(Parlance.CSharp.Analyzers.Rules.PARL0001_PreferPrimaryConstructors).Assembly;
        analyzers.AddRange(parlAssembly.DiscoverInstances<DiagnosticAnalyzer>());

        // Load upstream analyzer DLLs
        var analyzerDir = ResolveAnalyzerDirectory(targetFramework);
        if (analyzerDir is not null && Directory.Exists(analyzerDir))
        {
            foreach (var dllPath in Directory.EnumerateFiles(analyzerDir, "*.dll"))
            {
                // Skip resource assemblies
                if (dllPath.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var loadContext = new AssemblyLoadContext(Path.GetFileName(dllPath), isCollectible: false);
                    loadContext.Resolving += (alc, assemblyName) =>
                        ResolveFromDirectory(alc, assemblyName, analyzerDir);

                    var assembly = loadContext.LoadFromAssemblyPath(dllPath);
                    analyzers.AddRange(assembly.DiscoverInstances<DiagnosticAnalyzer>());
                }
                catch
                {
                    // Skip DLLs that fail to load (dependency-only assemblies, etc.)
                }
            }
        }

        return analyzers.ToImmutable();
    }

    private static Assembly? ResolveFromDirectory(
        AssemblyLoadContext context, AssemblyName assemblyName, string directory)
    {
        // Try exact match first
        var candidatePath = Path.Combine(directory, assemblyName.Name + ".dll");
        if (File.Exists(candidatePath))
            return context.LoadFromAssemblyPath(candidatePath);

        // Check for prefixed DLLs (e.g. Roslynator_Analyzers_Roslynator.Core.dll)
        foreach (var file in Directory.EnumerateFiles(directory, $"*{assemblyName.Name}.dll"))
        {
            try
            {
                return context.LoadFromAssemblyPath(file);
            }
            catch
            {
                // Continue trying other candidates
            }
        }

        // Fall back to default context for framework/Roslyn assemblies
        return null;
    }

    private static string? ResolveAnalyzerDirectory(string targetFramework)
    {
        // Walk up from the executing assembly location to find the project root
        // (directory containing a .csproj file), then look for analyzer-dlls/{tfm}/
        var assemblyLocation = typeof(AnalyzerLoader).Assembly.Location;
        var dir = Path.GetDirectoryName(assemblyLocation);

        while (dir is not null)
        {
            if (Directory.GetFiles(dir, "*.csproj").Length > 0)
            {
                var analyzerPath = Path.Combine(dir, "analyzer-dlls", targetFramework);
                if (Directory.Exists(analyzerPath))
                    return analyzerPath;
            }

            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: try relative to the assembly location in case of test scenarios
        // where build output is nested deeply
        var srcDir = FindDirectoryAbove(Path.GetDirectoryName(assemblyLocation), "src");
        if (srcDir is not null)
        {
            var analyzerPath = Path.Combine(srcDir, "Parlance.Analyzers.Upstream", "analyzer-dlls", targetFramework);
            if (Directory.Exists(analyzerPath))
                return analyzerPath;
        }

        return null;
    }

    private static string? FindDirectoryAbove(string? startDir, string targetName)
    {
        var dir = startDir;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, targetName);
            if (Directory.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
