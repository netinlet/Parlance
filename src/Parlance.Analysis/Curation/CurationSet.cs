using System.Collections.Immutable;

namespace Parlance.Analysis.Curation;

public sealed record CurationSet(
    string Name,
    string Description,
    ImmutableList<CurationRule> Rules,
    ImmutableList<CurationRationale> Rationales);
