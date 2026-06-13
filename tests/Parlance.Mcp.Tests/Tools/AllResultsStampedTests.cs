using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class AllResultsStampedTests
{
    private const long Sentinel = 9_999_999L;

    [Fact]
    public void EveryToolResult_HasSnapshotVersionProperty()
    {
        var resultTypes = typeof(SearchSymbolsResult).Assembly.GetTypes()
            .Where(t => t.Namespace == "Parlance.Mcp.Tools"
                        && t.Name.EndsWith("Result", StringComparison.Ordinal)
                        && !t.IsAbstract);

        var missing = resultTypes
            .Where(t => t.GetProperty("SnapshotVersion") is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(missing.Count == 0, "Missing SnapshotVersion: " + string.Join(", ", missing));
    }

    // The existence check above cannot catch the real defect: a factory that accepts a
    // snapshotVersion but never assigns it (so the result ships SnapshotVersion = 0 despite running
    // over a known snapshot). This invokes every factory that takes a `long snapshotVersion` and
    // asserts the value reaches the property.
    [Fact]
    public void EveryFactory_TakingSnapshotVersion_WiresItToTheProperty()
    {
        var factories = typeof(SearchSymbolsResult).Assembly.GetTypes()
            .Where(t => t.Namespace == "Parlance.Mcp.Tools"
                        && t.Name.EndsWith("Result", StringComparison.Ordinal)
                        && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.ReturnType == m.DeclaringType
                        && m.GetParameters().Any(p => p.Name == "snapshotVersion" && p.ParameterType == typeof(long)))
            .ToList();

        Assert.NotEmpty(factories);

        var failures = new List<string>();
        foreach (var factory in factories)
        {
            var args = factory.GetParameters()
                .Select(p => p.Name == "snapshotVersion" ? Sentinel : BuildArg(p.ParameterType))
                .ToArray();

            var result = factory.Invoke(null, args)!;
            var stamped = (long)result.GetType().GetProperty("SnapshotVersion")!.GetValue(result)!;
            if (stamped != Sentinel)
                failures.Add($"{factory.DeclaringType!.Name}.{factory.Name} -> {stamped}");
        }

        Assert.True(failures.Count == 0,
            "Factories that accept snapshotVersion but drop it: " + string.Join(", ", failures));
    }

    // The wiring test above only sees factories that already accept a snapshotVersion. This catches
    // the complementary gap the original test was blind to: a loaded no-result factory (not_found,
    // ambiguous, no_docs, no_fixes, …) that never accepts the version at all, so it silently ships
    // SnapshotVersion = 0 — the not-loaded sentinel — from a live workspace. Invoke every factory,
    // read its Status, and require a real stamp on every outcome that isn't a genuine sentinel.
    [Fact]
    public void EveryLoadedOutcome_CarriesSnapshotVersion()
    {
        // Version-0 is legitimate only for: the pre-load sentinels (not_loaded/load_failed);
        // input-validation rejections (error — raised before a session is in hand for some tools);
        // and stale (carries the *actual* current version through its own parameter, not "snapshotVersion").
        var exemptStatuses = new HashSet<string> { "not_loaded", "load_failed", "error", "stale" };

        var factories = typeof(SearchSymbolsResult).Assembly.GetTypes()
            .Where(t => t.Namespace == "Parlance.Mcp.Tools"
                        && t.Name.EndsWith("Result", StringComparison.Ordinal)
                        && !t.IsAbstract
                        // workspace-status owns a bespoke lifecycle (loading/failed are first-class
                        // payloads, not query outcomes) and stamps the version on its loaded path itself.
                        && t != typeof(WorkspaceStatusResult))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.ReturnType == m.DeclaringType)
            .ToList();

        var failures = new List<string>();
        foreach (var factory in factories)
        {
            object result;
            try
            {
                var args = factory.GetParameters()
                    .Select(p => p.Name == "snapshotVersion" ? Sentinel : BuildArg(p.ParameterType))
                    .ToArray();
                result = factory.Invoke(null, args)!;
            }
            catch
            {
                continue; // factory needs args we can't synthesize here — not the no-result surface
            }

            if (result.GetType().GetProperty("Status")?.GetValue(result) is not string status)
                continue;
            if (exemptStatuses.Contains(status))
                continue;

            var stamped = (long)result.GetType().GetProperty("SnapshotVersion")!.GetValue(result)!;
            if (stamped != Sentinel)
                failures.Add($"{factory.DeclaringType!.Name}.{factory.Name} (status={status}) -> {stamped}");
        }

        Assert.True(failures.Count == 0,
            "Loaded outcomes not stamped with the snapshot version: " + string.Join(", ", failures));
    }

    // Minimal stand-in values so a factory can be invoked purely to verify version wiring.
    private static object? BuildArg(Type type)
    {
        if (type == typeof(string)) return "x";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableList<>))
            return type.GetField("Empty", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        if (type.IsValueType) return Activator.CreateInstance(type); // int/bool/long/RepoPath/Nullable<>
        if (typeof(IEnumerable).IsAssignableFrom(type)) return null;
        return null; // reference-typed records (e.g. summaries) — not enforced at runtime
    }
}
