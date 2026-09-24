using System.Security.Cryptography;
using OpsReconciliation.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Store>();

var app = builder.Build();

app.UseExceptionHandler(exceptionApp => exceptionApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." });
}));
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/import/orders", (IFormFile? file, Store store) => Import(file, "orders", store)).DisableAntiforgery();
app.MapPost("/api/import/payments", (IFormFile? file, Store store) => Import(file, "payments", store)).DisableAntiforgery();
app.MapPost("/api/reconcile", (ReconcileRequest request, Store store) =>
{
    if (request.OrderBatchId <= 0 || request.PaymentBatchId <= 0 || request.Tolerance < 0)
        return Results.BadRequest(new { error = "Batch IDs must be positive and tolerance non-negative." });

    try { return Results.Ok(store.Run(request.OrderBatchId, request.PaymentBatchId, request.Tolerance ?? 0.01m)); }
    catch (KeyNotFoundException) { return Results.NotFound(new { error = "One or more batches were not found." }); }
    catch (ValidationException exception) { return Results.BadRequest(new { error = exception.Message }); }
});
app.MapGet("/api/runs", (Store store) => Results.Ok(store.Runs()));
app.MapGet("/api/runs/{id:int}", (int id, Store store) =>
    id <= 0 ? Results.BadRequest(new { error = "Run ID must be positive." }) : store.Detail(id) is { } run ? Results.Ok(run) : Results.NotFound(new { error = "Run not found." }));
app.MapGet("/api/runs/{id:int}/export.csv", (int id, Store store) =>
    id <= 0 ? Results.BadRequest(new { error = "Run ID must be positive." }) : store.Detail(id) is { } run ? Results.File(System.Text.Encoding.UTF8.GetBytes(ReportCsv.Create(run.Items)), "text/csv", $"reconciliation-run-{id}.csv") : Results.NotFound(new { error = "Run not found." }));
app.MapGet("/api/audit", (Store store) => Results.Ok(store.Audit()));
app.Run();

static IResult Import(IFormFile? file, string kind, Store store)
{
    if (file is null || file.Length == 0) return Results.BadRequest(new { error = "A non-empty CSV file is required." });
    if (file.Length > 1_048_576) return Results.BadRequest(new { error = "File exceeds the 1 MiB limit." });
    if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "Only CSV uploads are accepted." });

    try
    {
        using var content = new MemoryStream();
        file.CopyTo(content);
        var hash = Convert.ToHexString(SHA256.HashData(content.ToArray())).ToLowerInvariant();
        content.Position = 0;
        return Results.Ok(kind == "orders" ? store.ImportOrders(hash, CsvNormalizer.Orders(content)) : store.ImportPayments(hash, CsvNormalizer.Payments(content)));
    }
    catch (ValidationException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch { return Results.BadRequest(new { error = "The CSV could not be processed." }); }
}

public partial class Program;
