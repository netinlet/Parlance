namespace Parlance.Analysis.Tests.Fixtures.LiveRefactor;

// Mutable DTO refactor target. Caret on the type name (line 7, col 14) offers:
//   * Extract interface  -> ILineItem
//   * Generate constructor from members
//   * Generate equality members / ToString
public class LineItem
{
    public string Sku { get; set; } = "";

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }
}
