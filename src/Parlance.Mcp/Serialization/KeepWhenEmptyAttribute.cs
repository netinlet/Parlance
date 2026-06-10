namespace Parlance.Mcp.Serialization;

/// <summary>
/// Opt a collection property out of the global empty-collection drop (see
/// <see cref="ParlanceToolJson"/>). Use when an empty <c>[]</c> is itself a signal the client must
/// be able to distinguish from an absent/never-populated field — e.g. an "analyzed, zero
/// diagnostics" result.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class KeepWhenEmptyAttribute : Attribute;
