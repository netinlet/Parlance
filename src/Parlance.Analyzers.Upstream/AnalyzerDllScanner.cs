using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

namespace Parlance.Analyzers.Upstream;

internal static class AnalyzerDllScanner
{
    internal static readonly HashSet<string> SupportedFrameworks = ["net8.0", "net10.0"];

    private static readonly ConcurrentDictionary<string, ImmutableArray<Assembly>> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static ImmutableArray<Assembly> ScanAssemblies(string targetFramework) =>
        Cache.GetOrAdd(targetFramework, ScanAssembliesCore);

    private static ImmutableArray<Assembly> ScanAssembliesCore(string targetFramework)
    {
        var analyzerDir = ResolveAnalyzerDirectory(targetFramework);
        if (analyzerDir is null || !Directory.Exists(analyzerDir))
            return [];

        var builder = ImmutableArray.CreateBuilder<Assembly>();

        foreach (var dllPath in Directory.EnumerateFiles(analyzerDir, "*.dll"))
        {
            if (dllPath.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
                continue;

            Assembly assembly;
            try
            {
                var loadContext = new AssemblyLoadContext(Path.GetFileName(dllPath), isCollectible: false);
                loadContext.Resolving += (alc, assemblyName) =>
                {
                    var local = ResolveFromDirectory(alc, assemblyName, analyzerDir);
                    if (local is not null) return local;

                    // Fall back to the default ALC for BCL/runtime assemblies.
                    // On net10.0 the BCL shims (e.g. Microsoft.Bcl.AsyncInterfaces) are built-in,
                    // so the default ALC will resolve them regardless of the requested version.
                    try { return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName); }
                    catch { return null; }
                };
                assembly = loadContext.LoadFromAssemblyPath(dllPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load assembly from '{dllPath}': {ex.Message}");
                continue;
            }

            builder.Add(assembly);
        }

        return builder.ToImmutable();
    }

    private static Assembly? ResolveFromDirectory(
        AssemblyLoadContext context, AssemblyName assemblyName, string directory)
    {
        var candidatePath = Path.Combine(directory, assemblyName.Name + ".dll");
        if (File.Exists(candidatePath))
            return context.LoadFromAssemblyPath(candidatePath);

        foreach (var file in Directory.EnumerateFiles(directory, $"*{assemblyName.Name}.dll"))
        {
            try { return context.LoadFromAssemblyPath(file); }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to resolve '{assemblyName.Name}' from '{file}': {ex.Message}");
            }
        }

        return null;
    }

    private static string? ResolveAnalyzerDirectory(string targetFramework)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(AnalyzerDllScanner).Assembly.Location);

        if (assemblyDir is not null)
        {
            var localBase = Path.Combine(assemblyDir, "analyzer-dlls");
            var localPath = Path.Combine(localBase, targetFramework);
            if (Directory.Exists(localPath))
                return localPath;

            // Fallback: try other supported TFMs (e.g., net10.0 CLI analyzing net8.0 projects)
            if (Directory.Exists(localBase))
            {
                var fallback = SupportedFrameworks
                    .Where(f => f != targetFramework)
                    .Select(f => Path.Combine(localBase, f))
                    .FirstOrDefault(Directory.Exists);
                if (fallback is not null)
                    return fallback;
            }
        }

        var srcDir = FindDirectoryAbove(assemblyDir, "src");
        if (srcDir is not null)
        {
            var devBase = Path.Combine(srcDir, "Parlance.Analyzers.Upstream", "analyzer-dlls");
            var devPath = Path.Combine(devBase, targetFramework);
            if (Directory.Exists(devPath))
                return devPath;

            // Fallback in dev scenario too
            if (Directory.Exists(devBase))
            {
                var fallback = SupportedFrameworks
                    .Where(f => f != targetFramework)
                    .Select(f => Path.Combine(devBase, f))
                    .FirstOrDefault(Directory.Exists);
                if (fallback is not null)
                    return fallback;
            }
        }

        return null;
    }

    private static string? FindDirectoryAbove(string? startDir, string targetName)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, targetName)))
                return Path.Combine(dir, targetName);
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
