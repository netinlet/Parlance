using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Parlance.Analysis.Curation;

public sealed class CurationSetProvider(ILogger<CurationSetProvider> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private ImmutableDictionary<string, CurationSet>? _cache;

    public CurationSet? Load(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var sets = EnsureLoaded();
        return sets.GetValueOrDefault(name);
    }

    public ImmutableList<string> Available()
    {
        var sets = EnsureLoaded();
        return [.. sets.Keys.Order()];
    }

    private ImmutableDictionary<string, CurationSet> EnsureLoaded()
    {
        if (_cache is not null)
            return _cache;

        var builder = ImmutableDictionary.CreateBuilder<string, CurationSet>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(CurationSetProvider).Assembly;
        var prefix = "Parlance.Analysis.Curation.Sets.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".json"))
                continue;

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null) continue;

                var set = JsonSerializer.Deserialize<CurationSet>(stream, JsonOptions);
                if (set is not null)
                    builder[set.Name] = set;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load curation set from {Resource}", resourceName);
            }
        }

        _cache = builder.ToImmutable();
        return _cache;
    }
}
