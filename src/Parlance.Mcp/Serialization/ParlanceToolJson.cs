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
        // Layer the modifier ON TOP of the SDK resolver (source-gen) instead of prepending a fresh
        // reflection DefaultJsonTypeInfoResolver. Prepending one shadows the source-gen context for
        // every tool DTO and breaks AOT/trimming; WithAddedModifier wraps the existing resolver and
        // only post-processes the JsonTypeInfo it returns.
        var baseResolver = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
        options.TypeInfoResolver = baseResolver.WithAddedModifier(DropEmptyCollections);
        foreach (var converter in extraConverters)
            options.Converters.Add(converter);
        return options;
    }

    // WhenWritingNull does not suppress "[]"; this does. Scoped to list/array/set-shaped properties:
    // dictionaries are left alone so an always-present (possibly empty) map still emits "{}" rather
    // than vanishing into `undefined` on the client.
    private static void DropEmptyCollections(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            var type = property.PropertyType;
            if (type == typeof(string) ||
                !typeof(IEnumerable).IsAssignableFrom(type) ||
                IsDictionary(type))
                continue;

            var inner = property.ShouldSerialize;
            property.ShouldSerialize = (obj, value) =>
                (inner is null || inner(obj, value)) && value is IEnumerable e && HasAny(e);
        }
    }

    private static bool IsDictionary(Type type) =>
        typeof(IDictionary).IsAssignableFrom(type) ||
        type.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

    private static bool HasAny(IEnumerable enumerable)
    {
        if (enumerable is ICollection collection) return collection.Count > 0;
        var e = enumerable.GetEnumerator();
        try { return e.MoveNext(); }
        finally { (e as IDisposable)?.Dispose(); }
    }
}
