namespace Parlance.CSharp.Analyzers.Metrics;

/// <summary>
/// Centralized numeric defaults for the complexity metrics and the analyzers
/// that consume them. Every tweakable value that appeared as an inline literal
/// in the walker or rule code belongs here, so that thresholds and increment
/// weights are reviewed and changed in one place rather than hunted down
/// across files.
/// </summary>
internal static class ComplexityDefaults
{
    // ─── Thresholds (PARL3001) ────────────────────────────────────────────
    // Configurable per-project via .editorconfig:
    //   dotnet_code_quality.PARL3001.max_cognitive_complexity           → method-ish
    //   dotnet_code_quality.PARL3001.max_cognitive_complexity.property  → accessor-ish
    // These constants are the fallback defaults when the option is unset.

    /// <summary>
    /// Default cognitive-complexity threshold for methods, constructors,
    /// destructors, operators, conversion operators, and local functions.
    /// Matches Sonar S3776's method default.
    /// </summary>
    public const int MethodThreshold = 15;

    /// <summary>
    /// Default cognitive-complexity threshold for property/indexer arrow
    /// bodies and accessor bodies (get/set/init/add/remove). The lower value
    /// reflects that accessors are conventionally trivial; anything more
    /// complex is a stronger smell than an equally-complex method. Matches
    /// Sonar S3776's property default.
    /// </summary>
    public const int PropertyThreshold = 3;

    // ─── Cognitive-complexity increment weights ──────────────────────────
    // These are the per-construct weights the cognitive walker adds to the
    // score. Where 0 and 1 are both defensible, the comment records which
    // upstream we match.

    /// <summary>
    /// Increment added by a <c>break</c> statement. 0 (Sonar S3776) and 1
    /// (JetBrains Cognitive Complexity plugin) are both defensible readings
    /// of the Sonar white paper. Parlance chose 1 for JetBrains parity; flip
    /// to 0 here to switch to Sonar parity.
    /// </summary>
    public const int BreakIncrement = 1;

    /// <summary>
    /// Increment added by a <c>continue</c> statement. Parlance does not
    /// score <c>continue</c>, matching both Sonar and JetBrains.
    /// </summary>
    public const int ContinueIncrement = 0;

    /// <summary>
    /// Increment added for each new logical-operator group (a run of the
    /// same <c>&amp;&amp;</c> or <c>||</c> in sequence). Applied flat, not
    /// nesting-weighted — this is Sonar's "new logical group" rule.
    /// </summary>
    public const int NewLogicalGroupIncrement = 1;

    /// <summary>
    /// Increment added once per method when a direct recursive call is
    /// detected inside it.
    /// </summary>
    public const int RecursionIncrement = 1;
}
