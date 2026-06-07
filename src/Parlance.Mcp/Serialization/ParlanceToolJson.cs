using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;

namespace Parlance.Mcp.Serialization;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> all MCP tool output flows through. Built from the
/// SDK defaults (camelCase + WhenWritingNull) plus two global payload wins:
/// relaxed escaping (C# generics/lambdas tokenize far better unescaped) and dropping empty collections.
/// </summary>
public static class ParlanceToolJson
{
    public static JsonSerializerOptions Create(params JsonConverter[] extraConverters)
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.TypeInfoResolverChain.Insert(0, new DefaultJsonTypeInfoResolver
        {
            Modifiers = { DropEmptyCollections },
        });
        foreach (var converter in extraConverters)
            options.Converters.Add(converter);
        return options;
    }

    // WhenWritingNull does not suppress "[]"; this does. Skips zero-count enumerables (but never strings).
    private static void DropEmptyCollections(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType == typeof(string) ||
                !typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                continue;

            var inner = property.ShouldSerialize;
            property.ShouldSerialize = (obj, value) =>
                (inner is null || inner(obj, value)) && value is IEnumerable e && HasAny(e);
        }
    }

    private static bool HasAny(IEnumerable enumerable)
    {
        if (enumerable is ICollection collection) return collection.Count > 0;
        var e = enumerable.GetEnumerator();
        try { return e.MoveNext(); }
        finally { (e as IDisposable)?.Dispose(); }
    }
}
