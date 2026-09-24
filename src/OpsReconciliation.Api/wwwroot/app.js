const batches = {};

function setText(selector, value) {
  document.querySelector(selector).textContent = value;
}

async function readJson(response) {
  const body = await response.json();
  if (!response.ok) {
    throw new Error(body.error ?? 'Request failed');
  }
  return body;
}

async function upload(kind) {
  const input = document.querySelector(`#${kind}`);
  const file = input.files[0];
  if (!file) return;

  const form = new FormData();
  form.append('file', file);

  try {
    const response = await fetch(`/api/import/${kind}`, {
      method: 'POST',
      body: form,
    });
    const result = await readJson(response);
    batches[kind] = result.batchId;
    setText(
      `#${kind}Result`,
      `batch ${result.batchId} (${result.idempotent ? 'reused' : 'imported'})`,
    );
  } catch (error) {
    setText(`#${kind}Result`, error instanceof Error ? error.message : 'Upload failed');
  }
}

function renderSummary(summary, runId) {
  const container = document.querySelector('#summary');
  container.replaceChildren();

  const summaryText = Object.entries(summary)
    .map(([type, count]) => `${type}: ${count}`)
    .join(' · ');
  container.append(document.createTextNode(summaryText + ' '));

  const link = document.createElement('a');
  link.href = `/api/runs/${runId}/export.csv`;
  link.textContent = 'Export CSV';
  container.append(link);
}

function renderItems(items) {
  const tbody = document.querySelector('#items');
  tbody.replaceChildren();

  for (const item of items) {
    const row = document.createElement('tr');
    for (const value of [
      item.resultType,
      item.identifier,
      item.reasonCode,
      item.message,
    ]) {
      const cell = document.createElement('td');
      cell.textContent = value ?? '';
      row.append(cell);
    }
    tbody.append(row);
  }
}

async function run() {
  if (!batches.orders || !batches.payments) {
    setText('#summary', 'Upload both orders and payments before reconciliation.');
    return;
  }

  try {
    const response = await fetch('/api/reconcile', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        orderBatchId: batches.orders,
        paymentBatchId: batches.payments,
        tolerance: 0.01,
      }),
    });
    const result = await readJson(response);

    const detail = await readJson(await fetch(`/api/runs/${result.runId}`));
    const audit = await readJson(await fetch('/api/audit'));

    renderSummary(result.summary, result.runId);
    renderItems(detail.items);
    setText('#audit', JSON.stringify(audit, null, 2));
  } catch (error) {
    setText('#summary', error instanceof Error ? error.message : 'Reconciliation failed');
  }
}
