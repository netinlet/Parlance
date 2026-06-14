using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace Parlance.Analyzers.Upstream;

public enum TrustCheckResult { NotFound, Trusted, HashMismatch }

public sealed record TrustedDllEntry(string DllPath, string Hash);

/// <summary>
/// Machine-local or repo-local trust store for external analyzer DLLs.
/// Format: flat JSON <c>{ "abs/path/to.dll": "sha256:hexhash" }</c>.
/// <para>
/// Two conventional paths:
/// <list type="bullet">
/// <item><see cref="ProjectPath"/> — per-repo, committable for team sharing.</item>
/// <item><see cref="GlobalPath"/> — machine-local, applies to all repos.</item>
/// </list>
/// </para>
/// Fail-closed: a DLL not in the file, or whose hash no longer matches, is untrusted.
/// </summary>
public sealed class AnalyzerTrustFile(string path)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    // Trust keys are canonical file paths. The filesystem is case-sensitive on Linux but
    // case-insensitive on Windows/macOS, where MSBuild/Roslyn can surface a DLL path with
    // different casing than what was trusted — so the comparer must follow the platform.
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static string ProjectPath(string repoRoot) =>
        Path.Combine(repoRoot, ".parlance", "trusted_analyzers.json");

    public static readonly string GlobalPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".parlance", "trusted_analyzers.json");

    /// <summary>Checks whether a DLL is trusted and its hash matches the stored value.</summary>
    public TrustCheckResult Check(string absoluteDllPath)
    {
        var data = ReadSafe();
        var key = Canonical(absoluteDllPath);
        if (!data.TryGetValue(key, out var storedHash)) return TrustCheckResult.NotFound;
        if (!File.Exists(absoluteDllPath)) return TrustCheckResult.NotFound;
        return Hash(absoluteDllPath) == storedHash
            ? TrustCheckResult.Trusted
            : TrustCheckResult.HashMismatch;
    }

    /// <summary>
    /// Cheap fingerprint of the trust file state (mtime + size).
    /// Changes whenever the file is written; safe to use as a cache key.
    /// </summary>
    public string Fingerprint()
    {
        if (!File.Exists(path)) return "empty";
        var info = new FileInfo(path);
        return $"{info.LastWriteTimeUtc.Ticks}:{info.Length}";
    }

    /// <summary>Hashes <paramref name="absoluteDllPath"/> and records the grant.</summary>
    public void Trust(string absoluteDllPath) => TrustMany([absoluteDllPath]);

    /// <summary>Trusts every analyzer <c>*.dll</c> in <paramref name="dir"/> (skips <c>*.resources.dll</c>).</summary>
    public void TrustDirectory(string dir) => TrustMany(EnumerateAnalyzerDlls(dir));

    /// <summary>
    /// Hashes and records a grant for each DLL in one read-modify-write cycle. Trusting a whole
    /// directory is O(1) file rewrites rather than O(n) deserialize/serialize of an ever-growing file.
    /// </summary>
    public void TrustMany(IEnumerable<string> absoluteDllPaths)
    {
        var data = ReadForWrite();
        var changed = false;
        foreach (var dll in absoluteDllPaths)
        {
            var key = Canonical(dll);
            var hash = Hash(dll);
            if (!data.TryGetValue(key, out var existing) || existing != hash)
            {
                data[key] = hash;
                changed = true;
            }
        }
        if (changed) Write(data);
    }

    /// <summary>Removes the trust grant for <paramref name="absoluteDllPath"/>.</summary>
    public void Revoke(string absoluteDllPath) => RevokeMany([absoluteDllPath]);

    /// <summary>Revokes every analyzer <c>*.dll</c> in <paramref name="dir"/> (skips <c>*.resources.dll</c>).</summary>
    public void RevokeDirectory(string dir) => RevokeMany(EnumerateAnalyzerDlls(dir));

    /// <summary>Removes the trust grant for each DLL in one read-modify-write cycle.</summary>
    public void RevokeMany(IEnumerable<string> absoluteDllPaths)
    {
        var data = ReadForWrite();
        var changed = false;
        foreach (var dll in absoluteDllPaths)
            changed |= data.Remove(Canonical(dll));
        if (changed) Write(data);
    }

    /// <summary>Returns all stored trust entries.</summary>
    public ImmutableList<TrustedDllEntry> List() =>
        ReadSafe().Select(kv => new TrustedDllEntry(kv.Key, kv.Value)).ToImmutableList();

    /// <summary>
    /// Enumerates analyzer DLLs in <paramref name="dir"/>, skipping satellite <c>*.resources.dll</c>
    /// assemblies — the same filter every loader applies, so what is trusted matches what loads.
    /// </summary>
    public static IEnumerable<string> EnumerateAnalyzerDlls(string dir) =>
        Directory.EnumerateFiles(dir, "*.dll")
            .Where(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase));

    // Read path (Check/List): fail-closed. A missing, unreadable, or corrupt file is treated as
    // "nothing trusted" rather than crashing a routine analyze/workspace-status.
    private Dictionary<string, string> ReadSafe()
    {
        if (!File.Exists(path)) return Empty();
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return Empty();
        }
    }

    // Mutating path (Trust/Revoke): never clobber. A present-but-corrupt file throws instead of
    // silently resetting to {} and discarding every prior grant on the next Write.
    private Dictionary<string, string> ReadForWrite()
    {
        if (!File.Exists(path)) return Empty();
        var text = File.ReadAllText(path); // IO/permission errors propagate — do not overwrite blindly
        try
        {
            return Parse(text);
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException(
                $"Trust file '{path}' is not valid JSON; refusing to overwrite it and discard existing grants. Fix or delete it, then retry.",
                e);
        }
    }

    private static Dictionary<string, string> Parse(string json)
    {
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return parsed is null ? Empty() : new(parsed, PathComparer);
    }

    private static Dictionary<string, string> Empty() => new(PathComparer);

    private void Write(Dictionary<string, string> data)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
    }

    private static string Canonical(string dllPath) => Path.GetFullPath(dllPath);

    private static string Hash(string dllPath)
    {
        using var stream = File.OpenRead(dllPath);
        var bytes = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
