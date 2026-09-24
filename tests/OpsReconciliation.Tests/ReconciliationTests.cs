using System.Text;
using OpsReconciliation.Core;

namespace OpsReconciliation.Tests;

public class ReconciliationTests
{
    [Fact]
    public void Parses_quoted_csv_and_invariant_money()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("order_id,amount,currency,description\nA,12.50,USD,\"a, quoted \"\"note\"\"\"\n"));
        var record = CsvNormalizer.Orders(stream).Single();
        Assert.Equal(12.50m, record.Amount);
        Assert.Equal("a, quoted \"note\"", record.Description);
    }

    [Fact]
    public void Rejects_non_invariant_money()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("id,amount,currency\nA,\"1,20\",USD\n"));
        Assert.Throws<ValidationException>(() => CsvNormalizer.Orders(stream));
    }

    [Fact]
    public void Produces_all_six_rules_deterministically()
    {
        var orders = new[] { new OrderRecord("a", 10, "USD", ""), new OrderRecord("b", 10, "USD", ""), new OrderRecord("c", 10, "USD", ""), new OrderRecord("d", 10, "USD", ""), new OrderRecord("e", 10, "USD", "") };
        var payments = new[] { new PaymentRecord("a", 10, "USD", "1"), new PaymentRecord("c", 12, "USD", "1"), new PaymentRecord("d", 10, "EUR", "1"), new PaymentRecord("e", 10, "USD", "1"), new PaymentRecord("e", 10, "USD", "2"), new PaymentRecord("z", 7, "USD", "1") };
        var result = Reconciler.Reconcile(orders, payments);

        Assert.Equal(new[] { "MATCHED", "MISSING_PAYMENT", "AMOUNT_MISMATCH", "CURRENCY_MISMATCH", "DUPLICATE_PAYMENT", "ORPHAN_PAYMENT" }.OrderBy(type => type), result.Select(item => item.ResultType).OrderBy(type => type));
        Assert.Equal(result.Select(item => item.Identifier).OrderBy(id => id), result.Select(item => item.Identifier));
    }

    [Fact]
    public void Export_escapes_comma_quote_newline()
    {
        var csv = ReportCsv.Create([new("MATCHED", "X", "hello, \"world\"\nnext", "a", 1, 1, 0, "USD")]);
        Assert.Contains("\"hello, \"\"world\"\"\nnext\"", csv);
    }
}
