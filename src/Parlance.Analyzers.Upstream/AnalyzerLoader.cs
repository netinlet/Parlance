using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parlance.Analyzers.Upstream;

public static class AnalyzerLoader
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
        var parlAssembly = typeof(Parlance.CSharp.Analyzers.Rules.PARL9003_UseDefaultLiteral).Assembly;
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
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load analyzer from '{dllPath}': {ex.Message}");
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to resolve '{assemblyName.Name}' from '{file}': {ex.Message}");
            }
        }

        // Fall back to default context for framework/Roslyn assemblies
        return null;
    }

    private static string? ResolveAnalyzerDirectory(string targetFramework)
    {
        var assemblyLocation = typeof(AnalyzerLoader).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation);

        // Packaged/installed scenario: analyzer-dlls next to the executing assembly
        if (assemblyDir is not null)
        {
            var localPath = Path.Combine(assemblyDir, "analyzer-dlls", targetFramework);
            if (Directory.Exists(localPath))
                return localPath;
        }

        // Development scenario: walk up to find the Parlance.Analyzers.Upstream project
        var srcDir = FindDirectoryAbove(assemblyDir, "src");
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
