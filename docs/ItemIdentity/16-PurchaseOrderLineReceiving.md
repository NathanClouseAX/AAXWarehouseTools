# Item Identity Scenario 16 — Purchase Order Line Receiving

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

Receivers select the purchase order **line** rather than scanning the item, and should see the vendor's number (or the variant) to confirm the goods.

## What the worker sees

```
PO Line Receive
Item:            W0001
Color / Size:    Black / Large
Vendor item:     FAB-HS200-BK-L
Qty:             5 ea
```

## Supported flows

Purchase order line receiving and **purchase order line receiving and locate**.

## Prerequisites

USMF has no line-receiving menu item. Create one under **Warehouse management > Setup > Mobile device > Mobile device menu items**:

| Field | Value |
|---|---|
| Menu item name / Title | `PO Line Receive` |
| Mode | Work |
| Use existing work | No |
| Work creation process | Purchase order line receiving |

Add it to the inbound menu that user **24** uses (**Mobile device menu**). Also D3 (US-101 → `FAB-HS200-BK-L`) and PO D9 (US-101, warehouse 24, W0001 Black/Large qty 5, confirmed).

## Configure

**Item identity menu item settings**: `PO Line Receive` → `PARTNER`, *Label override* = `Vendor item`. The *Partner context* column shows **Vendor**.

## Try it on USMF

Open **PO Line Receive**, enter PO D9 and line 1, and read the quantity/confirmation screen.

## Expected results

| Order | Worker sees |
|---|---|
| D9 (US-101) | `FAB-HS200-BK-L` |
| D10 (US-104, no record) | `Wired Headset - Large / Black` |

## How it resolves

Identical to item receiving ([scenario 02](02-VendorItemNumberOnReceiving.md)): the vendor is the purchase order's vendor account, and the product dimensions come from the purchase order line.

## Troubleshooting

| Symptom | Check |
|---|---|
| No line | The new menu item has no settings row, or the row uses the title instead of the menu item name. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 3.
