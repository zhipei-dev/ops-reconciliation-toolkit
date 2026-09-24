using OpsReconciliation.Core;

public sealed record ReconcileRequest(long OrderBatchId, long PaymentBatchId, decimal? Tolerance);
public sealed record ImportReply(long BatchId, bool Idempotent, int RecordsImported);
public sealed record RunReply(long RunId, int ItemCount, Dictionary<string, int> Summary);
public sealed record RunDetail(long RunId, long OrderBatchId, long PaymentBatchId, decimal Tolerance, DateTimeOffset CreatedUtc, IReadOnlyList<ReconciliationItem> Items);
