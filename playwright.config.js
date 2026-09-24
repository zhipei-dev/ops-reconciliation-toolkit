const { defineConfig } = require('@playwright/test');
const path = require('node:path');

const host = process.env.E2E_HOST || '127.0.0.1';
const port = process.env.E2E_PORT || '5180';
const baseURL = `http://${host}:${port}`;
const databasePath = path.join('data', `e2e-${process.pid}-${Date.now()}.db`);

module.exports = defineConfig({
  testDir: './e2e',
  use: { baseURL },
  webServer: {
    command: `dotnet run --no-launch-profile --project src/OpsReconciliation.Api --urls ${baseURL}`,
    url: `${baseURL}/api/health`,
    reuseExistingServer: false,
    env: { ...process.env, DatabasePath: databasePath },
  },
  reporter: 'list',
});
