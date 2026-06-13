namespace Parlance.Analysis.Tests.Fixtures;

// Minimal, intentional extract-interface target for the option-gated refactoring tests —
// replaces the former gitignored scratch/LiveRefactor dependency. Public members so
// "Extract interface" is offered and member selection (PickMembers) is exercisable.
// Caret for the tests: line 7, column 14 — the type identifier below.
public class RefactorSample
{
    public string Name { get; set; } = "";

    public decimal Amount { get; set; }
}
