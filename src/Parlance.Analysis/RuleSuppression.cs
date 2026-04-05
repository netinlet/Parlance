namespace Parlance.Analysis;

public sealed class RuleSuppression
{
    private readonly HashSet<string> _ids;

    public static readonly RuleSuppression None = new([]);

    private RuleSuppression(IEnumerable<string> ruleIds) =>
        _ids = new HashSet<string>(ruleIds, StringComparer.OrdinalIgnoreCase);

    public static RuleSuppression From(IEnumerable<string> ruleIds) => new(ruleIds);

    public bool IsSuppressed(string ruleId) => _ids.Contains(ruleId);
    public bool IsEmpty => _ids.Count == 0;
}
