# Item Identity Scenario 15 — Put-Away, Transfer, Production and Replenishment Work

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

You want the identity line on warehouse work that is not sales picking — purchase put-away after receipt, transfer order picking, production raw-material picking, replenishment. These flows have **no trading partner**, so the line shows partner-independent identities (variant, GTIN, barcode, search name, item number).

## What the worker sees

```
Purchase Put-away
Item:            W0001
Variant:         Wired Headset - Large / Black
```

## Supported flows

| USMF menu item | Work transaction type | Partner context column |
|---|---|---|
| Purchase Put-away | Purchase order | None |
| Transfer pick | Transfer issue | None |
| Production pick | Raw material picking | None |
| Replenishment | Replenishment | None |

The line appears on their pick and put screens (user directed, system directed and grouping menu items).

## Prerequisites

| Record | USMF value |
|---|---|
| Received purchase order | D9 (US-101, W0001 Black/Large) received with **Purchase Receive**, so put-away work exists |

## Configure

1. **Item identity profiles**: `VARIANT` — 10 *Variant product name*, Append dimension summary = Yes (or `PARTNER`, whose external line will simply be skipped).
2. **Item identity menu item settings**: `Purchase Put-away` → `VARIANT`, *Label override* = `Variant`.

Assigning a profile whose only line is *External item number* to these menu items raises a warning on save (partner context *None*): that combination can never resolve.

## Try it on USMF

Put away the license plate received from D9 with **Purchase Put-away** (user **24**).

## Expected results

| Profile | Worker sees |
|---|---|
| VARIANT | `Wired Headset - Large / Black` |
| PARTNER | `Wired Headset - Large / Black` — not `FAB-HS200-BK-L`: purchase work carries no vendor |
| EXTONLY | fallback (no line with *Hide*) |

## How it resolves

The partner is taken from the work: only **sales** work provides a customer. Every other work type resolves without a partner, so *External item number* lines report *no partner context* and the chain moves on.

## Troubleshooting

| Symptom | Check |
|---|---|
| Expected the vendor's number on put-away | Not available on work execution; show it at receipt instead ([scenario 02](02-VendorItemNumberOnReceiving.md)). |
| A menu item with both sales and non-sales work classes shows *Customer* | Correct for its sales work; its other work still resolves without a partner. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenarios 6 (c) and 20 (c).
