# Item Identity Scenario 07 — Show the GTIN

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

Cases and cartons carry GS1 GTIN labels, and the worker verifies the goods by comparing the printed GTIN with the screen — without scanning it as a separate step.

## What the worker sees

```
Item:            W0001
Color / Size:    Black / Large
GTIN:            0614141000104
```

## Supported flows

All supported flows. No partner is needed.

## Prerequisites

USMF ships **no** GTINs. Register them on **Released products** > item > **Manage inventory** > **Warehouse** > **GTIN codes** (D5; unit left blank):

| Item | Variant | GTIN |
|---|---|---|
| W0001 | Black / Large | `0614141000104` |
| W0001 | Black / Medium | `0614141000111` |
| W0001 | Black / Small | `0614141000128` |
| W0001 | Red / Large | `0614141000135` |
| W0001 | Red / Medium | `0614141000142` |
| W0001 | Red / Small | `0614141000159` |
| A0001 | *(item level)* | `0614141000203` |

These use the GS1 example company prefix `0614141` with valid check digits — test values only. Sales order D6 (US-001, warehouse 24: W0001 Black/Large and A0001), released.

## Configure

1. **Item identity profiles**: `GTIN` — 10 *GTIN*, 20 *Item number*.
2. **Item identity menu item settings**: `Sales Picking` → `GTIN`, *Label override* = `GTIN`.

## Try it on USMF

Pick both lines of D6 with **Sales Picking** (user **24**).

## Expected results

| Pick | Registrations | Worker sees |
|---|---|---|
| W0001 Black/Large | variant GTIN | `0614141000104` |
| A0001 | item-level GTIN | `0614141000203` |
| W0001 Black/Large, plus a second GTIN with a unit | unit-blank + unit-specific | `0614141000104` (unit-blank wins) |
| W0001 Black/Large, variant GTINs deleted, item-level GTIN `0614141000210` added | item level only | `0614141000210` |
| Any item without GTINs | none | line 20: the item number |

## How it resolves

1. Registration for the item and the variant's **product dimensions** (storage and tracking dimensions of the work line are ignored) — unit-blank first, then any unit.
2. Otherwise the item-level registration (blank dimensions) — unit-blank first, then any unit.

## Troubleshooting

| Symptom | Check |
|---|---|
| Item number shows instead of the GTIN | No registration for that variant nor at item level; Test resolution reports *no GTIN record*. |
| A unit-specific GTIN shows instead of the expected one | No unit-blank registration exists for that variant. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 18. Barcodes: [scenario 08](08-ItemBarcode.md).
