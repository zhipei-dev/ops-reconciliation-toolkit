# Reconciliation rules

Identifier is the only join key. `MATCHED` requires equal currencies and an absolute decimal delta within the named tolerance (default `0.01`). An order without payments is `MISSING_PAYMENT`; a payment without an order is `ORPHAN_PAYMENT`. A same-currency delta outside tolerance is `AMOUNT_MISMATCH`. Different currencies are `CURRENCY_MISMATCH`; no FX is attempted. More than one payment for one order is `DUPLICATE_PAYMENT`. Results sort by identifier then type and each stores a code, message, expected, actual, delta, and currency where applicable.
