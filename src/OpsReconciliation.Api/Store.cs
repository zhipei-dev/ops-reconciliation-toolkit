using System.Globalization;
using Microsoft.Data.Sqlite;
using OpsReconciliation.Core;

public sealed class Store
{
    private readonly string connectionString;

    public Store(IConfiguration configuration)
    {
        var databasePath = configuration["DatabasePath"] ?? "data/ops-reconciliation.db";
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        using var connection = Open();
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS import_batches(id INTEGER PRIMARY KEY,kind TEXT NOT NULL,hash TEXT NOT NULL,n INTEGER NOT NULL,utc TEXT NOT NULL,UNIQUE(kind,hash));
            CREATE TABLE IF NOT EXISTS orders(id INTEGER PRIMARY KEY,b INTEGER,external_id TEXT,amount TEXT,currency TEXT,description TEXT);
            CREATE TABLE IF NOT EXISTS payments(id INTEGER PRIMARY KEY,b INTEGER,external_id TEXT,amount TEXT,currency TEXT,reference TEXT);
            CREATE TABLE IF NOT EXISTS reconciliation_runs(id INTEGER PRIMARY KEY,ob INTEGER,pb INTEGER,tolerance TEXT,utc TEXT);
            CREATE TABLE IF NOT EXISTS reconciliation_items(id INTEGER PRIMARY KEY,r INTEGER,type TEXT,code TEXT,message TEXT,identifier TEXT,expected TEXT,actual TEXT,delta TEXT,currency TEXT);
            CREATE TABLE IF NOT EXISTS audit_events(id INTEGER PRIMARY KEY,type TEXT,detail TEXT,utc TEXT);
            """);
    }

    public ImportReply ImportOrders(string hash, IReadOnlyList<OrderRecord> records) => Import(hash, "orders", records, null);
    public ImportReply ImportPayments(string hash, IReadOnlyList<PaymentRecord> records) => Import(hash, "payments", null, records);

    public RunReply Run(long orderBatchId, long paymentBatchId, decimal tolerance)
    {
        var orders = ReadOrders(orderBatchId);
        var payments = ReadPayments(paymentBatchId);
        if (orders is null || payments is null) throw new KeyNotFoundException();

        var items = Reconciler.Reconcile(orders, payments, tolerance);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, "INSERT INTO reconciliation_runs(ob,pb,tolerance,utc) VALUES($o,$p,$t,$u)", ("$o", orderBatchId), ("$p", paymentBatchId), ("$t", Format(tolerance)), ("$u", UtcNow()));
        var runId = LastInsertRowId(connection);
        foreach (var item in items)
        {
            Execute(connection, "INSERT INTO reconciliation_items(r,type,code,message,identifier,expected,actual,delta,currency) VALUES($r,$t,$c,$m,$i,$e,$a,$d,$u)",
                ("$r", runId), ("$t", item.ResultType), ("$c", item.ReasonCode), ("$m", item.Message), ("$i", item.Identifier),
                ("$e", item.ExpectedAmount is null ? null : Format(item.ExpectedAmount.Value)), ("$a", item.ActualAmount is null ? null : Format(item.ActualAmount.Value)),
                ("$d", item.Delta is null ? null : Format(item.Delta.Value)), ("$u", item.Currency));
        }
        AddAudit(connection, "RECONCILE", $"run {runId}");
        transaction.Commit();
        return new RunReply(runId, items.Count, items.GroupBy(item => item.ResultType).ToDictionary(group => group.Key, group => group.Count()));
    }

    public IEnumerable<object> Runs()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,ob,pb,tolerance,utc FROM reconciliation_runs ORDER BY id DESC";
        using var reader = command.ExecuteReader();
        var results = new List<object>();
        while (reader.Read()) results.Add(new { id = reader.GetInt64(0), orderBatchId = reader.GetInt64(1), paymentBatchId = reader.GetInt64(2), tolerance = reader.GetString(3), createdUtc = reader.GetString(4) });
        return results;
    }

    public RunDetail? Detail(long runId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ob,pb,tolerance,utc FROM reconciliation_runs WHERE id=$i";
        command.Parameters.AddWithValue("$i", runId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new RunDetail(runId, reader.GetInt64(0), reader.GetInt64(1), decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(3)), ReadItems(connection, runId));
    }

    public IEnumerable<object> Audit()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,type,detail,utc FROM audit_events ORDER BY id DESC";
        using var reader = command.ExecuteReader();
        var results = new List<object>();
        while (reader.Read()) results.Add(new { id = reader.GetInt64(0), type = reader.GetString(1), detail = reader.GetString(2), createdUtc = reader.GetString(3) });
        return results;
    }

    private ImportReply Import(string hash, string kind, IReadOnlyList<OrderRecord>? orders, IReadOnlyList<PaymentRecord>? payments)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var existing = Scalar(connection, "SELECT id FROM import_batches WHERE kind=$k AND hash=$h", ("$k", kind), ("$h", hash));
        if (existing is not null) { transaction.Commit(); return new ImportReply(Convert.ToInt64(existing), true, 0); }

        var recordCount = orders?.Count ?? payments!.Count;
        Execute(connection, "INSERT INTO import_batches(kind,hash,n,utc) VALUES($k,$h,$n,$u)", ("$k", kind), ("$h", hash), ("$n", recordCount), ("$u", UtcNow()));
        var batchId = LastInsertRowId(connection);
        if (orders is not null) foreach (var record in orders) Execute(connection, "INSERT INTO orders(b,external_id,amount,currency,description) VALUES($b,$i,$a,$c,$d)", ("$b", batchId), ("$i", record.ExternalId), ("$a", Format(record.Amount)), ("$c", record.Currency), ("$d", record.Description));
        if (payments is not null) foreach (var record in payments) Execute(connection, "INSERT INTO payments(b,external_id,amount,currency,reference) VALUES($b,$i,$a,$c,$d)", ("$b", batchId), ("$i", record.ExternalId), ("$a", Format(record.Amount)), ("$c", record.Currency), ("$d", record.Reference));
        AddAudit(connection, "IMPORT", $"{kind} batch {batchId}");
        transaction.Commit();
        return new ImportReply(batchId, false, recordCount);
    }

    private List<OrderRecord>? ReadOrders(long batchId) => ReadRecords(batchId, "orders", "SELECT external_id,amount,currency,description FROM orders WHERE b=$b", reader => new OrderRecord(reader.GetString(0), Parse(reader, 1)!.Value, reader.GetString(2), reader.GetString(3)));
    private List<PaymentRecord>? ReadPayments(long batchId) => ReadRecords(batchId, "payments", "SELECT external_id,amount,currency,reference FROM payments WHERE b=$b", reader => new PaymentRecord(reader.GetString(0), Parse(reader, 1)!.Value, reader.GetString(2), reader.GetString(3)));

    private List<T>? ReadRecords<T>(long batchId, string kind, string sql, Func<SqliteDataReader, T> map)
    {
        using var connection = Open();
        if (Scalar(connection, "SELECT id FROM import_batches WHERE id=$b AND kind=$k", ("$b", batchId), ("$k", kind)) is null) return null;
        using var command = connection.CreateCommand(); command.CommandText = sql; command.Parameters.AddWithValue("$b", batchId);
        using var reader = command.ExecuteReader(); var records = new List<T>();
        while (reader.Read()) records.Add(map(reader));
        return records;
    }

    private static List<ReconciliationItem> ReadItems(SqliteConnection connection, long runId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type,code,message,identifier,expected,actual,delta,currency FROM reconciliation_items WHERE r=$i ORDER BY identifier,type";
        command.Parameters.AddWithValue("$i", runId);
        using var reader = command.ExecuteReader(); var items = new List<ReconciliationItem>();
        while (reader.Read()) items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), Parse(reader, 4), Parse(reader, 5), Parse(reader, 6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return items;
    }

    private SqliteConnection Open() { var connection = new SqliteConnection(connectionString); connection.Open(); return connection; }
    private static decimal? Parse(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : decimal.Parse(reader.GetString(index), CultureInfo.InvariantCulture);
    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string UtcNow() => DateTimeOffset.UtcNow.ToString("O");
    private static long LastInsertRowId(SqliteConnection connection) => Convert.ToInt64(Scalar(connection, "SELECT last_insert_rowid()"));
    private static void AddAudit(SqliteConnection connection, string type, string detail) => Execute(connection, "INSERT INTO audit_events(type,detail,utc) VALUES($t,$d,$u)", ("$t", type), ("$d", detail), ("$u", UtcNow()));
    private static object? Scalar(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters) { using var command = connection.CreateCommand(); command.CommandText = sql; AddParameters(command, parameters); return command.ExecuteScalar(); }
    private static void Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters) { using var command = connection.CreateCommand(); command.CommandText = sql; AddParameters(command, parameters); command.ExecuteNonQuery(); }
    private static void AddParameters(SqliteCommand command, IEnumerable<(string Name, object? Value)> parameters) { foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value); }
}
