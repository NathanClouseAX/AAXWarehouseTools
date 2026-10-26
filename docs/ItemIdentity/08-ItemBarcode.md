# Item Identity Scenario 08 — Show an Item Barcode

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

Goods carry a barcode per variant (for example an EAN-13 per size), and the worker compares the printed code with the screen.

## What the worker sees

```
Item:            D0013
Size:            M
Barcode:         0012345678929
```

## Supported flows

All supported flows. No partner is needed.

## Prerequisites

| Record | USMF value |
|---|---|
| Item barcodes | **D0013** *SpecialSpeaker*: EAN-13 `0012345678912` (size S), `0012345678929` (M), `0012345678936` (L), all with *Use for input* (scanning) enabled — present on the reference USMF data; create them on **Released products** > D0013 > **Manage inventory** > **Warehouse** > **Bar code** if missing |
| On-hand | D0013 has **no** on-hand in USMF. For a device test, adjust in a few of each size in warehouse 24 and create/release a sales order line; otherwise use Test resolution. |

## Configure

1. **Item identity profiles**: `BARCODE` — 10 *Item barcode* (optionally 20 *Item number*).
2. **Item identity menu item settings**: `Sales Picking` → `BARCODE`, *Label override* = `Barcode`.

## Try it on USMF

Without a device: **Item identity profiles** > `BARCODE` > **Test resolution**, item `D0013`, *Product dimension number* = the inventory dimension of each size (on the reference database `000852` S, `000850` M, `000851` L — ids differ between environments).

## Expected results

| Variant | Registrations | Result |
|---|---|---|
| D0013 S | one scanning barcode | `0012345678912` |
| D0013 M | one scanning barcode | `0012345678929` |
| D0013 L | one scanning barcode | `0012345678936` |
| D0013 M, plus a non-scanning barcode for M | scanning + non-scanning | `0012345678929` (scanning wins) |
| Item with only an item-level barcode | item level | that barcode |
| W0001 (no barcodes in USMF) | none | *no barcode record* — line 20 or the fallback |

## How it resolves

1. Barcode for the item and the variant's product dimensions — a barcode with *Use for input* enabled first, then any.
2. Otherwise the item-level barcode (blank dimensions), with the same preference.

The barcode's unit and barcode setup are not considered.

## Troubleshooting

| Symptom | Check |
|---|---|
| *no barcode record* although a barcode exists | It is registered on a dimension combination that does not match the variant (for example only on size M when picking size L). |
| The "wrong" barcode of two shows | Both have the same *Use for input* setting; the first registration found wins — keep one scanning barcode per variant. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 18. GTINs: [scenario 07](07-GTIN.md).
