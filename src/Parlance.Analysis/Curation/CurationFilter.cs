using System.Collections.Immutable;

namespace Parlance.Analysis.Curation;

public static class CurationFilter
{
    public static bool Matches(CurationRule rule, string ruleId, string category)
    {
        if (rule.RuleId is not null)
        {
            if (rule.RuleId.EndsWith('*'))
            {
                var prefix = rule.RuleId[..^1];
                if (ruleId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(rule.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (rule.Category is not null)
            return string.Equals(rule.Category, category, StringComparison.OrdinalIgnoreCase);

        return false;
    }

    public static ImmutableList<CuratedDiagnostic> Apply(
        CurationSet? set, ImmutableList<CuratedDiagnostic> diagnostics)
    {
        if (set is null)
            return diagnostics;

        var rationaleMap = set.Rationales
            .ToImmutableDictionary(r => r.RationaleId, r => r.Message);

        var result = ImmutableList.CreateBuilder<CuratedDiagnostic>();

        foreach (var d in diagnostics)
        {
            var matchingRule = set.Rules.FirstOrDefault(r => Matches(r, d.RuleId, d.Category));
            if (matchingRule is null)
                continue;

            var severity = matchingRule.Severity ?? d.Severity;
            var fixClassification = matchingRule.FixClassification ?? d.FixClassification;
            var rationale = matchingRule.RationaleId is not null && rationaleMap.TryGetValue(matchingRule.RationaleId, out var msg)
                ? msg
                : d.Rationale;

            result.Add(d with { Severity = severity, FixClassification = fixClassification, Rationale = rationale });
        }

        return result.ToImmutable();
    }
}
