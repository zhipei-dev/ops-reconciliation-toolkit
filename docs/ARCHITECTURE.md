# Architecture

The Core library owns normalization, deterministic rule evaluation, and report serialization. The API owns multipart boundary checks, SQLite persistence, and generic HTTP errors. Every import writes its batch, normalized rows, and audit event in one transaction; every reconciliation run writes its run, items, and audit event in one transaction.

SQLite tables are `import_batches`, `order_records` (implemented as `orders`), `payment_records` (implemented as `payments`), `reconciliation_runs`, `reconciliation_items`, and `audit_events`. The two record table names are compact internal physical names; the logical model is unchanged.
