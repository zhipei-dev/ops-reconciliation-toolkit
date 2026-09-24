# Operations Reconciliation Toolkit

A public .NET 10 MVP for the operations-team problem of reconciling order exports against payment exports without opaque spreadsheet logic. It accepts synthetic CSV data, normalizes it, stores it in SQLite, produces deterministic and explainable outcomes, and exposes the result through an API and dashboard.

## Client fit

Relevant proof for CSV/data reconciliation, cross-system mismatch investigation, deterministic reporting, audit-trail work, and small operational data tools where explainability matters more than opaque spreadsheet formulas.

## Workflow

Upload synthetic orders and payments CSV files. Each returns a batch ID and whether it was newly imported or reused. Reconcile the two batch IDs; the dashboard presents a result-type summary, explainable rows, audit history, and an escaped CSV export. Imports use a SHA-256 content hash plus source kind as an idempotency key.

## Architecture

The ASP.NET Core minimal API hosts a static vanilla-JS dashboard. Core contains bounded CSV parsing, invariant `decimal` money normalization, deterministic rules, and CSV export. SQLite persists batches, normalized records, runs, result items, and audit events transactionally.

See:
- `docs/ARCHITECTURE.md`
- `docs/RECONCILIATION_RULES.md`
- `docs/SECURITY_AND_LIMITATIONS.md`

## Quick start

```bash
dotnet restore OpsReconciliation.sln
dotnet build OpsReconciliation.sln -c Release --no-restore
dotnet test tests/OpsReconciliation.Tests/OpsReconciliation.Tests.csproj -c Release --no-build
npm ci
npm run test:e2e
```


Start the API/dashboard locally:

```bash
dotnet run --project src/OpsReconciliation.Api/OpsReconciliation.Api.csproj --launch-profile http
```

Open `http://localhost:5078`.

Use `fixtures/orders.csv` and `fixtures/payments.csv` for the complete synthetic scenario.

## Validation

The test project contains 11 discovered tests covering quoted CSV parsing, invariant money parsing, all six reconciliation outcomes, deterministic ordering, CSV escaping, health, duplicate-import idempotency, API validation, and the complete import-to-export flow.

Playwright covers the browser workflow using a fresh SQLite database for each run. It defaults to `127.0.0.1`; environments where loopback access is restricted can set `E2E_HOST` to another reachable local interface before running `npm run test:e2e`. The override is test-only and is not required by normal environments or GitHub Actions.

## Reconciliation outcomes

The MVP produces six explicit outcomes: `MATCHED`, `MISSING_PAYMENT`, `ORPHAN_PAYMENT`, `AMOUNT_MISMATCH`, `CURRENCY_MISMATCH`, and `DUPLICATE_PAYMENT`. Each persisted item carries a reason code and explainable monetary fields where applicable.

## Limitations

All money is invariant-culture `decimal`; currencies are explicit and no FX conversion exists. Uploads are limited to 1 MiB CSV files. This is a single-process portfolio demonstration, not production-ready software, an accounting system, identity system, or high-volume ingestion platform.
## Handoff and support

- [Demo deployment and handoff](DEPLOYMENT.md)
- [Support](SUPPORT.md)
- [Security and limitations](docs/SECURITY_AND_LIMITATIONS.md)

