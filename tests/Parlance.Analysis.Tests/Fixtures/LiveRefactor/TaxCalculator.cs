using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Parlance.Analysis.Tests.Fixtures.LiveRefactor;

// Companion refactor target for the LiveRefactor model. Seeded opportunities (not all exercised yet —
// here so option-gated and structural refactorings have a richer surface to target over time):
//   * unused usings (System.Linq, System.Text)  -> Remove unnecessary usings (IDE0005)
//   * _baseRate assigned only in ctor           -> Make field readonly (IDE0044)
//   * if / else-if chain in RateForRegion       -> Convert to switch statement/expression
//   * the discount block in ComputeInvoiceTotal -> Extract method
public class TaxCalculator
{
    private decimal _baseRate;

    public TaxCalculator(decimal baseRate)
    {
        _baseRate = baseRate;
    }

    public decimal RateForRegion(string region)
    {
        if (region == "US")
        {
            return _baseRate + 0.05m;
        }
        else if (region == "EU")
        {
            return _baseRate + 0.20m;
        }
        else if (region == "UK")
        {
            return _baseRate + 0.19m;
        }
        else
        {
            return _baseRate;
        }
    }

    public decimal ComputeInvoiceTotal(IEnumerable<LineItem> items, string region)
    {
        decimal subtotal = 0m;
        foreach (LineItem item in items)
        {
            subtotal = subtotal + item.UnitPrice * item.Quantity;
        }

        // Cohesive block — a natural "Extract method" target (e.g. CalculateDiscount).
        decimal discount = 0m;
        if (subtotal > 1000m)
        {
            discount = subtotal * 0.10m;
        }
        else if (subtotal > 500m)
        {
            discount = subtotal * 0.05m;
        }
        decimal discounted = subtotal - discount;

        decimal rate = RateForRegion(region);
        decimal tax = discounted * rate;
        return discounted + tax;
    }
}
