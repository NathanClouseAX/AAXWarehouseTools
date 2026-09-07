# AAX Warehouse Tools — Item Identity Display User Guide

This guide is for the people who see the feature day to day: warehouse workers on the mobile app, and the power users who keep the configuration healthy. For first-time configuration, start with the [setup guide](ItemIdentitySetup.md).

## 1. What you see on the device

On configured screens, one extra read-only line appears with the other item information:

```
Item:            A0001
Item identity:   L102
Qty:             12 ea
```

- The **caption** (*Item identity* by default) is set by your administrator and can differ per menu item — sites often rename it to what it shows, such as *Customer item no.* or *Variant*.
- The **value** is the first identity that resolves from the configured chain — typically the trading partner's item number or the variant's product name. With the dimension-summary option on, the variant values are appended: `L102 - Black / M`.
- The line is **display only**. You cannot type into it, it is never a scan target, and it never stops you from completing a step.

## 2. Where it appears

| Screen | The value is resolved for |
|---|---|
| Picking work — pick and put screens (including the target license plate screens) | The customer on the sales order, when the work is sales work |
| Purchase order line receiving — item/quantity confirmation | The vendor on the purchase order |
| Purchase order item receiving — item/quantity confirmation | The vendor on the purchase order |
| Load item receiving | The vendor, when the load line comes from a purchase order |

Only menu items your administrator has configured show the line. Two workers on the same flow can see different captions (label override) but the same value.

## 3. What the value can be

The administrator defines an ordered chain. Normally the first source with a value wins; a profile can instead be set to *concatenate*, in which case every source that has a value appears, joined by a separator (for example `L102 | Golf Shirt - Black / M`). The sources are:

1. **External item number** — what *this* customer or vendor calls the item. Different partners see their own numbers.
2. **Variant product name** — the variant's name in your session language ("Golf Shirt - Midnight Black / M" instead of a bare code).
3. **GTIN** — the variant's registered GTIN.
4. **Item barcode** — a registered barcode for the variant.
5. **Search name** — the released product's search name.
6. **Item number** — the plain released product number.

## 4. When the line is absent

An absent line is normal in three situations:

- The menu item is not configured for item identity (most screens, by design).
- Nothing in the chain resolved and the fallback is *Hide* — for example, external-number-only sites picking an item this customer has no number for.
- The item is not known yet on the current screen (the line appears once the item is identified).

If the site prefers, the administrator can configure the fallback to show the item number or a fixed text instead of hiding.

## 5. Checking a value without a device — Test resolution

Power users can replay the exact resolution from the rich client:

1. Open **Warehouse management > Setup > Mobile device > Item identity profiles**, select the profile, and choose **Test resolution**.
2. Enter the item, optionally the product dimension number for a specific variant, and the partner the flow would supply — pick the context first, and the account lookup offers the matching customers or vendors (the field is disabled while the context is *None*).
3. Run. The log lists every source in chain order:

```
External item number - no value (no external item record)
Variant product name - resolved: Golf Shirt - Midnight Black / M
Winning source: Variant product name. Value: Golf Shirt - Midnight Black / M
```

The device shows exactly this winning value for the same inputs. Test resolution always resolves fresh (no cache), so it also answers "why does the device still show the old value" — see the next section.

## 6. Troubleshooting

| Symptom | Check |
|---|---|
| The line never appears anywhere | **Item identity parameters** — is *Enable item identity display* on? |
| The line is missing on one menu item | **Item identity menu item settings** — does the menu item have a row, and is it *Enabled* with a profile? Is the fallback *Hide* while nothing resolves (run Test resolution to see the per-source misses)? |
| The line shows the item number instead of the expected identity | The expected source missed — Test resolution shows why (no external item record for *that* partner, item has no product dimensions, empty search name, …). Also check the chain order: an earlier source may be winning. |
| The external item number never shows | External numbers need a partner. Movement-style flows have none, and non-sales work resolves without a customer — the source is skipped there by design. On picking, confirm the work belongs to a sales order. |
| A new GTIN / external number / renamed variant does not show yet | Resolved values are cached (default 30 minutes). Wait out the cache duration, or make any edit to the profile — that takes effect immediately. |
| The line sits in the wrong place on the screen | Placement is standard **Warehouse app field priority** setup, not AAX configuration — move the **Item identity** field there. |
| The caption is wrong on one menu item | That menu item has a **Label override** in its setting row. |

## 7. Performance

Resolution runs on the server during the normal screen build and is cached per item, variant, partner, and language. On a warm cache the added cost per screen is negligible; even the first (cold) resolution is a handful of indexed lookups. If a site ever suspects the feature in a slow step, disabling the menu item's setting row is an instant, zero-deployment way to test — the screen renders standard again on the next request.
