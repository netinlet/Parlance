using Microsoft.CodeAnalysis.CSharp;

namespace Parlance.CSharp;

internal static class LanguageVersionResolver
{
    /// <summary>
    /// Resolves a user-provided language version string to a Roslyn <see cref="LanguageVersion"/>.
    /// Uses <see cref="LanguageVersionFacts.TryParse"/> for friendly short names (e.g. "12")
    /// rather than <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/> which misinterprets
    /// numeric strings as raw enum values.
    /// </summary>
    public static LanguageVersion Resolve(string? version)
    {
        if (version is null)
            return LanguageVersion.Latest;

        if (LanguageVersionFacts.TryParse(version, out var parsed))
            return parsed;

        return LanguageVersion.Latest;
    }
}
