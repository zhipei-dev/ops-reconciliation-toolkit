using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace OpsReconciliation.Tests;

public sealed class ApiIntegrationTests
{
    [Fact]
    public async Task Health_returns_ok()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync("/api/health", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"ok\"", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Duplicate_order_import_reuses_the_original_batch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var first = await Import(client, "orders", OrdersCsv, cancellationToken);
        var second = await Import(client, "orders", OrdersCsv, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstJson = await Json(first, cancellationToken);
        var secondJson = await Json(second, cancellationToken);
        Assert.Equal(firstJson.GetProperty("batchId").GetInt64(), secondJson.GetProperty("batchId").GetInt64());
        Assert.True(secondJson.GetProperty("idempotent").GetBoolean());
        Assert.Equal(0, secondJson.GetProperty("recordsImported").GetInt32());
    }

    [Fact]
    public async Task Full_flow_persists_results_and_exports_csv()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var orderBatch = (await Json(
            await Import(client, "orders", OrdersCsv, cancellationToken),
            cancellationToken)).GetProperty("batchId").GetInt64();
        var paymentBatch = (await Json(
            await Import(client, "payments", PaymentsCsv, cancellationToken),
            cancellationToken)).GetProperty("batchId").GetInt64();

        var reconcile = await client.PostAsJsonAsync(
            "/api/reconcile",
            new { orderBatchId = orderBatch, paymentBatchId = paymentBatch, tolerance = 0.01m },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, reconcile.StatusCode);

        var runId = (await Json(reconcile, cancellationToken)).GetProperty("runId").GetInt64();

        var detail = await client.GetStringAsync($"/api/runs/{runId}", cancellationToken);
        Assert.Contains("MATCHED", detail);
        Assert.Contains("MISSING_PAYMENT", detail);
        Assert.Contains("AMOUNT_MISMATCH", detail);
        Assert.Contains("CURRENCY_MISMATCH", detail);
        Assert.Contains("DUPLICATE_PAYMENT", detail);
        Assert.Contains("ORPHAN_PAYMENT", detail);

        var export = await client.GetAsync($"/api/runs/{runId}/export.csv", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);

        var exportBody = await export.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("resultType,reasonCode", exportBody);
        Assert.Contains("MISSING_PAYMENT", exportBody);
    }

    [Theory]
    [InlineData("/api/runs/0")]
    [InlineData("/api/runs/-1")]
    [InlineData("/api/runs/0/export.csv")]
    public async Task Non_positive_run_ids_return_bad_request(string path)
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync(
            path,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_uploads_return_client_errors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        using var invalidExtension = new MultipartFormDataContent();
        invalidExtension.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes(OrdersCsv)),
            "file",
            "orders.txt");
        var extensionResponse = await client.PostAsync(
            "/api/import/orders",
            invalidExtension,
            cancellationToken);

        using var empty = new MultipartFormDataContent();
        empty.Add(new ByteArrayContent([]), "file", "orders.csv");
        var emptyResponse = await client.PostAsync(
            "/api/import/orders",
            empty,
            cancellationToken);

        using var oversized = new MultipartFormDataContent();
        oversized.Add(new ByteArrayContent(new byte[1_048_577]), "file", "orders.csv");
        var oversizedResponse = await client.PostAsync(
            "/api/import/orders",
            oversized,
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, extensionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, oversizedResponse.StatusCode);
    }

    private static async Task<HttpResponseMessage> Import(
        HttpClient client,
        string kind,
        string csv,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(
            new ByteArrayContent(Encoding.UTF8.GetBytes(csv)),
            "file",
            $"{kind}.csv");

        return await client.PostAsync($"/api/import/{kind}", form, cancellationToken);
    }

    private static async Task<JsonElement> Json(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken))
            .RootElement
            .Clone();

    private const string OrdersCsv = """
        order_id,amount,currency,description
        a,10.00,USD,matched
        b,10.00,USD,missing
        c,10.00,USD,amount mismatch
        d,10.00,USD,currency mismatch
        e,10.00,USD,duplicate
        """;

    private const string PaymentsCsv = """
        payment_id,amount,currency,reference
        a,10.00,USD,one
        c,12.00,USD,one
        d,10.00,EUR,one
        e,10.00,USD,one
        e,10.00,USD,two
        z,7.00,USD,orphan
        """;

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databasePath =
            Path.Combine(Path.GetTempPath(), $"ops-reconciliation-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabasePath"] = databasePath,
                }));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
