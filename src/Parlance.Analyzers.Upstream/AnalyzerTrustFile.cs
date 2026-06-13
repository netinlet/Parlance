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

    public static string ProjectPath(string repoRoot) =>
        Path.Combine(repoRoot, ".parlance", "trusted_analyzers.json");

    public static readonly string GlobalPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".parlance", "trusted_analyzers.json");

    /// <summary>Checks whether a DLL is trusted and its hash matches the stored value.</summary>
    public TrustCheckResult Check(string absoluteDllPath)
    {
        var data = Read();
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
    public void Trust(string absoluteDllPath)
    {
        var data = Read();
        data[Canonical(absoluteDllPath)] = Hash(absoluteDllPath);
        Write(data);
    }

    /// <summary>Calls <see cref="Trust"/> for every <c>*.dll</c> in <paramref name="dir"/>.</summary>
    public void TrustDirectory(string dir)
    {
        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
            Trust(dll);
    }

    /// <summary>Removes the trust grant for <paramref name="absoluteDllPath"/>.</summary>
    public void Revoke(string absoluteDllPath)
    {
        var data = Read();
        if (data.Remove(Canonical(absoluteDllPath)))
            Write(data);
    }

    /// <summary>Calls <see cref="Revoke"/> for every <c>*.dll</c> in <paramref name="dir"/>.</summary>
    public void RevokeDirectory(string dir)
    {
        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
            Revoke(dll);
    }

    /// <summary>Returns all stored trust entries.</summary>
    public ImmutableArray<TrustedDllEntry> List() =>
        [.. Read().Select(kv => new TrustedDllEntry(kv.Key, kv.Value))];

    private Dictionary<string, string> Read()
    {
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            return parsed is null ? new(StringComparer.Ordinal) : new(parsed, StringComparer.Ordinal);
        }
        catch (JsonException) { return new(StringComparer.Ordinal); }
    }

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
