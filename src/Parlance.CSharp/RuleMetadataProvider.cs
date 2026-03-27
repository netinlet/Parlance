using System.Collections.Frozen;

namespace Parlance.CSharp;

public sealed record RuleMetadata(
    string Category,
    string? Rationale,
    string? SuggestedFix);

public static class RuleMetadataProvider
{
    private static readonly FrozenDictionary<string, RuleMetadata> CuratedMetadata =
        new Dictionary<string, RuleMetadata>
        {
            ["PARL9003"] = new(
                "Modernization",
                "The default literal (C# 7.1+) lets the compiler infer the type from context, eliminating the redundant type argument in default(T) when the target type is already apparent.",
                "Replace 'default(T)' with 'default'."),
        }.ToFrozenDictionary();

    public static RuleMetadata? GetMetadata(string ruleId)
    {
        return CuratedMetadata.GetValueOrDefault(ruleId);
    }
}
