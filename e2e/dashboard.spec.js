const { test, expect } = require('@playwright/test');
const path = require('node:path');

test('uploads, reconciles, displays audit', async ({ page }) => {
  await page.goto('/');

  await page.locator('#orders').setInputFiles(path.join(__dirname, '../fixtures/orders.csv'));
  await page.getByRole('button', { name: 'Upload orders' }).click();
  await expect(page.locator('#ordersResult')).toContainText('batch');

  await page.locator('#payments').setInputFiles(path.join(__dirname, '../fixtures/payments.csv'));
  await page.getByRole('button', { name: 'Upload payments' }).click();

  await page.getByRole('button', { name: 'Run reconciliation' }).click();

  await expect(page.locator('#summary')).toContainText('MATCHED');
  await expect(page.locator('#items')).toContainText('DUPLICATE_PAYMENT');
  await expect(page.locator('#audit')).toContainText('RECONCILE');
});
