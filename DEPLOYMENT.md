# Demo deployment and handoff

This repository is a portfolio MVP. The following steps describe a local or controlled demo handoff, not a production accounting deployment.

## Runtime prerequisites

- .NET SDK 10
- Node.js 24 and npm only when running the browser E2E suite

## Build, test, and run

Restore, build, and test the application:

```bash
dotnet restore OpsReconciliation.sln
dotnet build OpsReconciliation.sln -c Release --no-restore
dotnet test tests/OpsReconciliation.Tests/OpsReconciliation.Tests.csproj -c Release --no-build
```

Start the API/dashboard project with its checked-in HTTP launch profile:

```bash
dotnet run --project src/OpsReconciliation.Api/OpsReconciliation.Api.csproj --launch-profile http
```

Then open:

```text
http://localhost:5078
```

The ASP.NET Core application serves both the API and static dashboard.

## Browser validation

For the optional browser flow:

```bash
npm ci
npm run test:e2e
```

The Playwright configuration uses a fresh SQLite database for validation. `E2E_HOST` is a test-only override for environments where the default loopback path is restricted.

## Handoff checklist

Before handing the demo to another developer:

1. run restore/build/test
2. run the API/dashboard with the documented project command
3. optionally run the browser E2E flow
4. provide the synthetic fixtures under `fixtures/`
5. do not represent the local SQLite persistence as production storage
6. review the reconciliation rules and security/limitations documents

## Production boundary

This project is not production-ready and is not an accounting system. It has no authentication/authorization, managed secrets, durable backup/retention policy, production observability, high-volume ingestion design, or compliance controls.

See:

- `docs/RECONCILIATION_RULES.md`
- `docs/SECURITY_AND_LIMITATIONS.md`
