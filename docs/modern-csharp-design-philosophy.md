<!-- generated from 20-Projects/Parlance/Contributor/modern-csharp-design-philosophy.md — edit in vault, run tools/docs/publish.sh -->

# Modern C# Design Philosophy for PARL Rules

Last updated: 2026-03-08
Status: Active design document

## Purpose

This document defines the **direction and philosophy** behind PARL rules. Every PARL rule should push code toward modern, idiomatic C# — reducing ceremony, embracing immutability, and adopting functional patterns. This is not a trend; it is a deliberate architectural evolution driven by the .NET ecosystem itself.

PARL rules exist to answer: **"If I were writing this code today, using the latest stable C# features, what would it look like?"**

## The Forces Driving Modern C#

### 1. Cloud-Native & Distributed Systems

Modern software runs as distributed microservices. This environment favors **immutable data** — data that doesn't change after creation — because it eliminates a massive category of bugs related to shared state and concurrency.

- Records were specifically designed to make this "functional" style easy and concise
- `ImmutableArray<T>`, `FrozenDictionary<TKey, TValue>`, and read-only collections enforce boundaries
- Value semantics (built into records) make equality predictable across service boundaries

**PARL implication:** Rules should nudge toward immutable-by-default patterns. Prefer records over mutable classes for data containers. Flag mutable arrays on immutable types (as we did with `AnalysisOptions.SuppressRules` — issue #21).

### 2. Boilerplate Reduction: Ceremony vs. Essence

Influencers like Zoran Horvat emphasize "modern C#" as a way to reduce **ceremony** — the structural code (constructors, getters, setters, equality overrides) that hides the **essence** of what the code actually does.

| Feature | Ceremony it eliminates | C# version |
|---------|----------------------|-------------|
| Primary constructors | Constructor body + field assignments | 12 |
| Collection expressions | `new List<T> { ... }`, `new T[] { ... }`, `Array.Empty<T>()` | 12 |
| File-scoped namespaces | One level of indentation across entire file | 10 |
| Target-typed `new` | Redundant type name on right side of declaration | 9 |
| Using declarations | Braces + indentation for `IDisposable` scope | 8 |
| Default literal | Redundant type name in `default(T)` | 7.1 |
| Records | Constructor + properties + equality + ToString + deconstruct | 9 |

**PARL implication:** Every PARL rule should be expressible as a before/after transformation. If you can't show a developer "here's your code, here's what it looks like with modern syntax," the rule isn't concrete enough.

### 3. Functional Programming Influence

C# has been absorbing functional concepts from F# for over a decade. This changes the mindset from "changing an object's state" to "transforming data from one form to another."

Key patterns:

- **Pattern matching** (`is`, `switch` expressions, `not`/`and`/`or` patterns) — replaces verbose type-checking and casting
- **Switch expressions** — treat branching as a value-returning operation, not a control-flow statement
- **Value equality** (built into records) — compare data, not memory addresses
- **Extension methods** — add behavior to types without inheritance, enabling fluent pipelines (as demonstrated in our `Assembly.DiscoverInstances<T>()` refactoring — issue #23)
- **Immutable transformations** — `with` expressions on records instead of mutation

**PARL implication:** Rules should favor expression-oriented code over statement-oriented code. Prefer switch expressions over switch statements. Prefer pattern matching over `is` + cast. Prefer extension methods over static helper methods when they improve readability.

### 4. High-Performance by Default

Modern .NET (from .NET 5 onwards) has a massive focus on performance. Many newer features give developers high-level abstractions without the performance penalties of older object-oriented patterns.

- `record struct` — value semantics without heap allocation
- `Span<T>` and `ReadOnlySpan<T>` — stack-allocated views over data
- Collection expressions — compiler chooses optimal backing type
- `FrozenDictionary` / `FrozenSet` — read-optimized collections
- `ref` fields and `ref struct` — zero-copy data access

**PARL implication:** When a modern feature is both more readable *and* more performant, that's a strong signal for a PARL rule. Collection expressions are a prime example — they're simultaneously more concise and allow the compiler to optimize the backing storage.

## Where Does "The Direction" Come From?

When influencers like Zoran say "the language is going this way," they aren't guessing. They are tracking the official, public design process:

### The C# Language Design Team (LDT)

Led by **Mads Torgersen** (C# Lead Designer) and **Dustin Campbell**, this group makes the final decisions on language evolution.

### Open Design on GitHub

All C# language proposals and debate happen in the [dotnet/csharplang](https://github.com/dotnet/csharplang) repository. The Language Design Meeting (LDM) notes document exactly why features are added or rejected. PARL rule authors should reference these notes when justifying new rules.

### Microsoft's Official Language Strategy

Microsoft publishes a [.NET Language Strategy](https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/strategy) that explicitly states their goal: keep C# relevant by evolving it to handle modern workloads — cloud, mobile, and high-performance services.

### How Influencers Track Direction

Influencers "get" the direction by:

- Reading LDM notes and proposal discussions
- Watching the Roslyn compiler evolve
- Observing how Microsoft's own libraries (Entity Framework, ASP.NET Core, the BCL) adopt new patterns first
- Testing preview features and reporting real-world feedback

**PARL implication:** When Microsoft's own libraries adopt a pattern, that's strong evidence it's the intended direction. When a pattern appears in LDM notes as a design goal, it's appropriate for a PARL rule. When a pattern is merely "possible" but not endorsed, it's too speculative.

## Before/After: The Core Transformation Catalog

Every PARL rule should be traceable to one of these transformation patterns. This catalog represents the "old → modern" progression that PARL rules enforce.

### Data Containers: Class → Record

```csharp
// OLD: 15+ lines of ceremony
public class Person {
    public string Name { get; set; }
    public int Age { get; set; }
    public Person(string name, int age) {
        Name = name;
        Age = age;
    }
    // Plus: Equals, GetHashCode, ToString overrides...
}

// MODERN: 1 line — immutable, value-equal, deconstructable
public record Person(string Name, int Age);
```

**Existing rule:** PARL0001 (Prefer primary constructors)
**Existing rule:** PARL0003 (Prefer required properties)

### Branching: Switch Statement → Switch Expression

```csharp
// OLD: Verbose, requires break, mutation-oriented
string message;
switch (status) {
    case Status.Active:
        message = "User is online";
        break;
    case Status.Inactive:
        message = "User is offline";
        break;
    default:
        message = "Unknown";
        break;
}

// MODERN: Expression-oriented, exhaustive, concise
var message = status switch {
    Status.Active => "User is online",
    Status.Inactive => "User is offline",
    _ => "Unknown"
};
```

**Existing rule:** PARL0005 (Use switch expression)

### Type Checking: is + Cast → Pattern Matching

```csharp
// OLD: Redundant type check and cast
if (obj is string) {
    var s = (string)obj;
    // use s
}

// MODERN: Combined check + declaration
if (obj is string s) {
    // use s
}
```

**Existing rule:** PARL0004 (Use pattern matching over is/cast)

### Collections: Multiple Syntaxes → Collection Expressions

```csharp
// OLD: Different syntax for every collection type
int[] oldArray = new int[] { 1, 2, 3 };
List<int> oldList = new List<int> { 1, 2, 3 };
int[] empty = Array.Empty<int>();

// MODERN: One unified syntax
int[] newArray = [1, 2, 3];
List<int> newList = [1, 2, 3];
int[] empty = [];
```

**Existing rule:** PARL0002 (Prefer collection expressions)

### Resource Management: Using Block → Using Declaration

```csharp
// OLD: Extra braces and indentation
using (var stream = File.OpenRead(path)) {
    using (var reader = new StreamReader(stream)) {
        // indented twice
    }
}

// MODERN: Disposed at end of enclosing scope
using var stream = File.OpenRead(path);
using var reader = new StreamReader(stream);
// flat, no extra indentation
```

**Existing rule:** PARL9001 (Use simple using declaration)

### Object Creation: Redundant Type → Target-Typed New

```csharp
// OLD: Type name repeated
Dictionary<string, List<int>> map = new Dictionary<string, List<int>>();

// MODERN: Compiler infers from left side
Dictionary<string, List<int>> map = new();
```

**Existing rule:** PARL9002 (Use implicit object creation)

### Defaults: Explicit Type → Default Literal

```csharp
// OLD: Type name is redundant noise
int x = default(int);
CancellationToken ct = default(CancellationToken);

// MODERN: Compiler infers from context
int x = default;
CancellationToken ct = default;
```

**Existing rule:** PARL9003 (Use default literal)

### Static Helpers → Extension Methods

```csharp
// OLD: Disconnected helper, reads inside-out
var analyzers = DiscoverAnalyzers(assembly);
var fixableIds = DiscoverFixableIds(parlAssembly);

// MODERN: Reads as natural operation on the object
var analyzers = assembly.DiscoverInstances<DiagnosticAnalyzer>();
var fixableIds = parlAssembly.DiscoverInstances<CodeFixProvider>()
    .SelectMany(fp => fp.FixableDiagnosticIds)
    .ToHashSet();
```

**Proposed rule:** GitHub issue #32

### Namespace Scoping: Block → File-Scoped

```csharp
// OLD: Entire file wrapped in braces
namespace MyProject.Models {
    public class Product {
        // indented one extra level
    }
}

// MODERN: Single line, no wrapping braces
namespace MyProject.Models;

public class Product {
    // starts at column 0
}
```

**Status:** Enforced via `.editorconfig`, no PARL rule needed

## Rule Design Principles

### 1. Always-Updating: Track the Latest Stable C#

PARL rules should target the **latest stable** C# version. When C# ships a new feature that replaces an older pattern, a new PARL rule should follow. The rule set is a living document that evolves with the language.

This means:

- Rules must gate on language version (don't suggest C# 12 features to a C# 10 project)
- Rules should be ready for the next C# release — monitor dotnet/csharplang proposals
- Old rules may need updating when a newer feature supersedes their suggestion

### 2. The Before/After Test

Every proposed PARL rule must pass this test:

> Can you show a concrete code snippet **before** and **after** the transformation, where the **after** is demonstrably more readable, more correct, or both?

If the before/after isn't compelling to a mid-level developer, the rule is too subtle or too aggressive.

### 3. Ceremony Reduction, Not Cleverness

PARL rules should make code **simpler**, not **shorter at all costs**. The goal is removing ceremony that adds no information, not compressing logic into fewer characters.

Good: `int[] items = [1, 2, 3]` replaces `int[] items = new int[] { 1, 2, 3 }` — same intent, less noise.
Bad: Chaining 5 LINQ methods into one expression — shorter but harder to debug.

### 4. Immutability as the Default Path

When a rule has two valid suggestions, prefer the one that leads toward immutability:

- `record` over `class` for data types
- `ImmutableArray<T>` over `T[]` on immutable types
- `init` over `set` for properties
- `required` over constructor parameters for simple initialization
- `with` expressions over mutation

### 5. Respect the Ecosystem's Own Adoption

When Microsoft's own libraries adopt a pattern, it's safe for a PARL rule. When the BCL, ASP.NET Core, or EF Core use a feature internally, that's the strongest signal that the pattern is intended and stable.

### 6. Experimental Rules: Leading, Not Just Following

Parlance doesn't have to wait for Microsoft to bless every pattern. When we discover a compelling transformation through real-world refactoring — like the extension method pattern from issue #32 — we can promote it as an **experimental** rule.

Experimental rules:

- Use a distinct severity or tag (e.g., `experimental` category, or an `PARLX` prefix) so developers know these are opinionated and forward-looking
- Require the same before/after evidence as stable rules, but the "proof" can come from our own codebase experience rather than ecosystem-wide adoption
- Are opt-in by default (disabled in `default` and `minimal` profiles, enabled in `strict` and `ai-agent`)
- Graduate to stable when the pattern shows up in Microsoft libraries, LDM discussions, or widespread community adoption
- Can be retired without stigma if they don't prove out

This is how Parlance stays ahead of the curve rather than just codifying what everyone already knows. The best linters don't just enforce consensus — they **teach patterns that developers haven't discovered yet**.

Examples of experimental-grade patterns:

- Static helper methods that read better as extension methods (issue #32)
- Duplicated code blocks that share a common first-parameter type — suggesting consolidation via generics + extensions
- `foreach` + manual accumulation patterns that would be clearer as LINQ pipelines
- Nested `if`/`else` chains that map cleanly to `switch` expressions but aren't obvious candidates
- Mutable builder patterns that could be replaced with `with` expressions on records

The bar for experimental rules: **"Would a senior developer, seeing the before/after, say 'that's clearly better'?"** If yes, ship it as experimental. Let adoption data and feedback determine graduation.

## Future Rule Candidates

Based on this philosophy, potential future PARL rules (beyond the current 8):

| Area | Pattern | C# Version | Before → After |
|------|---------|-------------|----------------|
| Immutability | Mutable collection on record | 9+ | `string[]` → `ImmutableArray<string>` |
| Refactoring | Static helper → extension method | 3+ | `Helper(obj)` → `obj.Helper()` |
| Pattern matching | Nested `if`/`else if` → `switch` expression | 8+ | Chain of `if` → `switch` |
| Pattern matching | `!= null` → `is not null` | 9+ | `x != null` → `x is not null` |
| Null handling | Null check + throw → `ArgumentNullException.ThrowIfNull` | 10+ | Guard clause → one-liner |
| LINQ | Manual loop → LINQ | 3+ | `foreach` + `if` + `Add` → `.Where().Select()` |
| String handling | `string.Format` → interpolation | 6+ | `string.Format("{0}", x)` → `$"{x}"` |
| String handling | Concatenation in loop → `StringBuilder` or `string.Join` | 1+ | `+=` in loop → `string.Join` |
| Raw strings | Escaped string → raw string literal | 11+ | `"line1\\nline2"` → `"""..."""` |
| Deconstruction | Multiple property access → deconstruct | 7+ | `var x = p.X; var y = p.Y;` → `var (x, y) = p;` |
| File-scoped types | `internal` helper class → `file` scoped | 11+ | `internal class Helper` → `file class Helper` |

## Relationship to Other Documents

- **`docs/analyzer-development-guide.md`** — How to *implement* analyzers correctly (Roslyn mechanics, testing, performance). Read that for the "how."
- **This document** — *What* analyzers should target and *why* (language philosophy, transformation catalog, design principles). Read this for the "what" and "why."
- **`docs/plans/2026-03-14-ide-for-ai-roadmap.md`** — Current product roadmap and architecture.

## Sources

- [C# Language Design Team — dotnet/csharplang](https://github.com/dotnet/csharplang)
- [.NET Language Strategy](https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/strategy)
- [Zoran Horvat — Modern C# patterns](https://codinghelmet.com/)
- [Mads Torgersen — C# Language Design](https://devblogs.microsoft.com/dotnet/author/madst/)
- Microsoft BCL, ASP.NET Core, and EF Core source — pattern adoption signals
- Refactoring experience from this codebase (issues #21, #23, #32)
