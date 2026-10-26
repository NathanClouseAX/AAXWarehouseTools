# Item Identity Scenario 02 — Vendor Item Number on Purchase Receiving

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

The receiver checks incoming goods against the vendor's packing slip or carton labels, which carry the vendor's item numbers. Showing the vendor's number on the receiving screen removes the cross-reference lookup.

## What the worker sees

```
Item:            W0001
Color / Size:    Black / Large
Vendor item:     FAB-HS200-BK-L
Qty:             5 ea
```

The line appears on the item and quantity/confirmation screens, once the item is identified. Screens before that (the purchase order number prompt) never carry it.

## Supported flows

**Purchase order item receiving** (USMF **Purchase Receive**) and its *and locate* variant. Purchase order line receiving and load item receiving work the same way — see [scenario 16](16-PurchaseOrderLineReceiving.md) and [scenario 17](17-LoadItemReceiving.md).

## Prerequisites

| Record | USMF value |
|---|---|
| Vendor external item description (D3) | Vendor **US-101** *Fabrikam Electronics*, item **W0001**, Black / Large → `FAB-HS200-BK-L`, description `Fabrikam HS-200 headset black large` |
| Purchase order (D9) | Vendor **US-101**, warehouse **24**, line W0001 Black/Large qty 5; **confirmed** |
| Purchase order (D10) | Vendor **US-104** *Fabrikam Supplier*, warehouse **24**, line W0001 Black/Large qty 5; confirmed (no external record — the negative case) |

Create D3 from **Released products** > W0001 > **Purchase** > **External item descriptions** (or from the vendor record).

## Configure

1. **Item identity parameters**: enabled; *Fallback behavior* = Hide; *Cache duration* = 0 while testing.
2. **Item identity profiles**: `PARTNER` — 10 *External item number*, 20 *Variant product name* (Append dimension summary), 30 *Item number*.
3. **Item identity menu item settings**: *Mobile device menu item* = `Purchase Receive`, *Enabled* = Yes, *Profile* = `PARTNER`, *Label override* = `Vendor item`. The *Partner context* column shows **Vendor**.

## Try it on USMF

1. Sign in as user **24**; open **Purchase Receive**.
2. Enter purchase order D9, then item W0001, color Black, size Large.
3. Read the quantity/confirmation screen. Repeat with D10.

## Expected results

| Order | Worker sees | Why |
|---|---|---|
| D9 — US-101 | `FAB-HS200-BK-L` | Exact-variant record for the vendor |
| D10 — US-104 | `Wired Headset - Large / Black` | No record for US-104; line 20 resolves. Another vendor's number is never shown. |

## How it resolves

- The partner is the purchase order's **vendor account** (the order account), as standard purchase order entry uses.
- The receipt's product dimensions come from the purchase order line being received, so a variant-specific external record matches.
- Precedence for vendor records is the same as for customers: account before vendor item group, exact variant before item level ([scenario 05](05-ExternalNumberPrecedence.md)).

## Troubleshooting

| Symptom | Check |
|---|---|
| Line shows the variant name for D9 | The external record's dimensions or vendor account do not match. [Test resolution](18-TestResolution.md) with context *Vendor*, account US-101. |
| Vendor number missing on **Purchase Put-away** | By design: put-away is work execution on purchase work, which carries no vendor, so the external source is skipped and the next line resolves — see [scenario 15](15-WorkWithoutPartner.md). |
| No line on **Transfer receive** or **RMA Receive** | Not supported flows. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 3.
