using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace OpsReconciliation.Core;

public sealed record OrderRecord(string ExternalId, decimal Amount, string Currency, string Description);
public sealed record PaymentRecord(string ExternalId, decimal Amount, string Currency, string Reference);
public sealed record ReconciliationItem(string ResultType, string ReasonCode, string Message, string Identifier, decimal? ExpectedAmount, decimal? ActualAmount, decimal? Delta, string? Currency);

public static class CsvNormalizer
{
    public static IReadOnlyList<OrderRecord> Orders(Stream source) => Read(source, (f, row) => new OrderRecord(Required(f, 0, row, "order_id"), Money(f, 1, row), Currency(f, 2, row), Optional(f, 3)));
    public static IReadOnlyList<PaymentRecord> Payments(Stream source) => Read(source, (f, row) => new PaymentRecord(Required(f, 0, row, "payment_id"), Money(f, 1, row), Currency(f, 2, row), Optional(f, 3)));
    private static IReadOnlyList<T> Read<T>(Stream source, Func<string[], int, T> map)
    {
        using var parser = new TextFieldParser(source, Encoding.UTF8, true) { TextFieldType = FieldType.Delimited, Delimiters = [","], HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        if (parser.EndOfData) throw new ValidationException("CSV is empty.");
        var header = parser.ReadFields() ?? [];
        if (header.Length < 3) throw new ValidationException("CSV must contain identifier, amount, currency columns.");
        var result = new List<T>(); var row = 1;
        while (!parser.EndOfData) { row++; var fields = parser.ReadFields() ?? []; if (fields.Length == 1 && string.IsNullOrWhiteSpace(fields[0])) continue; if (fields.Length < 3) throw new ValidationException($"Row {row} has too few columns."); result.Add(map(fields, row)); }
        if (result.Count == 0) throw new ValidationException("CSV has no data rows.");
        return result;
    }
    private static string Required(string[] f, int i, int row, string name) => string.IsNullOrWhiteSpace(Optional(f, i)) ? throw new ValidationException($"Row {row}: {name} is required.") : Optional(f, i).Trim();
    private static string Optional(string[] f, int i) => i < f.Length ? f[i] : "";
    private static decimal Money(string[] f, int i, int row) => decimal.TryParse(Required(f, i, row, "amount"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : throw new ValidationException($"Row {row}: amount must be invariant decimal.");
    private static string Currency(string[] f, int i, int row) { var c = Required(f, i, row, "currency").ToUpperInvariant(); return c.Length == 3 && c.All(char.IsLetter) ? c : throw new ValidationException($"Row {row}: currency must be a three-letter code."); }
}
public sealed class ValidationException(string message) : Exception(message);

public static class Reconciler
{
    public static IReadOnlyList<ReconciliationItem> Reconcile(IEnumerable<OrderRecord> orders, IEnumerable<PaymentRecord> payments, decimal tolerance = 0.01m)
    {
        if (tolerance < 0) throw new ValidationException("Tolerance must be non-negative.");
        var result = new List<ReconciliationItem>();
        var pg = payments.GroupBy(x => x.ExternalId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.OrderBy(p => p.Reference, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        foreach (var order in orders.OrderBy(x => x.ExternalId, StringComparer.Ordinal))
        {
            if (!pg.TryGetValue(order.ExternalId, out var ps)) { result.Add(new("MISSING_PAYMENT", "NO_PAYMENT", "No payment exists for this order.", order.ExternalId, order.Amount, null, null, order.Currency)); continue; }
            if (ps.Count > 1) { result.Add(new("DUPLICATE_PAYMENT", "MULTIPLE_PAYMENTS", $"{ps.Count} payments map to this order.", order.ExternalId, order.Amount, ps.Sum(x => x.Amount), ps.Sum(x => x.Amount) - order.Amount, order.Currency)); continue; }
            var p = ps[0];
            if (p.Currency != order.Currency) result.Add(new("CURRENCY_MISMATCH", "CURRENCY_DIFFERS", $"Order currency {order.Currency} differs from payment currency {p.Currency}; FX is not applied.", order.ExternalId, order.Amount, p.Amount, null, order.Currency));
            else { var delta = p.Amount - order.Amount; result.Add(Math.Abs(delta) <= tolerance ? new("MATCHED", "WITHIN_TOLERANCE", "Payment amount is within tolerance.", order.ExternalId, order.Amount, p.Amount, delta, order.Currency) : new("AMOUNT_MISMATCH", "AMOUNT_OUTSIDE_TOLERANCE", "Payment amount differs beyond tolerance.", order.ExternalId, order.Amount, p.Amount, delta, order.Currency)); }
        }
        var ids = orders.Select(x => x.ExternalId).ToHashSet(StringComparer.Ordinal);
        foreach (var p in payments.Where(x => !ids.Contains(x.ExternalId)).OrderBy(x => x.ExternalId, StringComparer.Ordinal).ThenBy(x => x.Reference, StringComparer.Ordinal)) result.Add(new("ORPHAN_PAYMENT", "NO_ORDER", "Payment has no corresponding order.", p.ExternalId, null, p.Amount, null, p.Currency));
        return result.OrderBy(x => x.Identifier, StringComparer.Ordinal).ThenBy(x => x.ResultType, StringComparer.Ordinal).ToList();
    }
}

public static class ReportCsv
{
    public static string Create(IEnumerable<ReconciliationItem> items)
    {
        static string E(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("resultType,reasonCode,message,identifier,expectedAmount,actualAmount,delta,currency\n");
        foreach (var x in items) sb.AppendJoin(',', E(x.ResultType), E(x.ReasonCode), E(x.Message), E(x.Identifier), E(x.ExpectedAmount?.ToString(CultureInfo.InvariantCulture)), E(x.ActualAmount?.ToString(CultureInfo.InvariantCulture)), E(x.Delta?.ToString(CultureInfo.InvariantCulture)), E(x.Currency)).Append('\n');
        return sb.ToString();
    }
}
