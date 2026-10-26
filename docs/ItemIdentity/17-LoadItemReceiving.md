# Item Identity Scenario 17 — Load Item Receiving

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

Inbound goods arrive on planned loads and are received against the load. The receiver should see the vendor's number for each load line.

## What the worker sees

```
Load Receive
Item:            W0001
Color / Size:    Black / Large
Vendor item:     FAB-HS200-BK-L
```

## Supported flows

Load item receiving and **load item receiving and locate**.

## Prerequisites

USMF has no load-receiving menu item. Create one under **Warehouse management > Setup > Mobile device > Mobile device menu items**:

| Field | Value |
|---|---|
| Menu item name / Title | `Load Receive` |
| Mode | Work |
| Use existing work | No |
| Work creation process | Load item receiving |

Add it to user **24**'s inbound menu. Then create a purchase order like D9 (US-101, warehouse 24, W0001 Black/Large qty 5), confirm it, and create an inbound load for it (**Load planning workbench**, or from the purchase order: **Warehouse** > **Add to new load**). D3 must exist.

## Configure

**Item identity menu item settings**: `Load Receive` → `PARTNER`, *Label override* = `Vendor item`. The *Partner context* column shows **Vendor**.

## Try it on USMF

Open **Load Receive**, enter the load id, then the item W0001, Black, Large; read the quantity/confirmation screen.

## Expected results

| Load line source | Worker sees |
|---|---|
| Purchase order from US-101 | `FAB-HS200-BK-L` |
| Purchase order from US-104 | `Wired Headset - Large / Black` |

## How it resolves

The load line traces to its purchase order line; the vendor is that order's vendor account and the product dimensions are the order line's. Load lines that do not come from a purchase order resolve without a partner.

## Troubleshooting

| Symptom | Check |
|---|---|
| Variant name instead of the vendor number | The load line's purchase order is from another vendor, or D3's dimensions differ. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 21.
