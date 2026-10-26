# Item Identity Scenario 01 — Customer Item Number on Sales Picking

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

A customer labels goods, shelves or orders with its own item numbers, and the picker must confirm the pick against the customer's number rather than your released product number. Typical for retail chains and private-label customers.

## What the worker sees

```
Item:            W0001
Color / Size:    Black / Large
Customer item:   CR-4471-BL
Qty:             2 ea
```

The line appears on the **pick** screen and again on the **put** screen (including the target license plate prompt).

## Supported flows

Every work menu item that executes **sales** work: user directed (USMF **Sales Picking**), system directed (**Sales Picking - System**), cluster picking (**Cluster Pick**), grouping (**Pick to Container**) and pick and pack. See [scenario 14](14-OtherPickingStyles.md) for the other picking styles.

## Prerequisites

| Record | USMF value |
|---|---|
| Customer external item description (D1) | Customer **US-001** *Contoso Retail San Diego*, item **W0001**, Color **Black**, Size **Large** → external item number `CR-4471-BL`, description `Contoso Retail headset black L` |
| Sales order (D6) | Customer **US-001**, site **2**, warehouse **24**, line W0001 Black/Large qty 2; **Release to warehouse** so picking work exists |
| Mobile user | **24** (*WH 24*, default warehouse 24) |

Create D1 from **Product information management > Products > Released products** > W0001 > **Sell** > **External item descriptions** (or from the customer record). Fill the product dimensions to make the number variant-specific.

## Configure

1. **Warehouse management > Setup > Mobile device > Item identity parameters**: *Enable item identity display* = Yes; *Fallback behavior* = Hide; *External item content* = Identifier; *Cache duration* = 0 while testing.
2. **Item identity profiles**: create `PARTNER` with lines 10 *External item number*, 20 *Variant product name* (Append dimension summary = Yes), 30 *Item number*. A profile with only the external line also works, but then customers without numbers get no line.
3. **Item identity menu item settings**: new row — *Mobile device menu item* = `Sales Picking`, *Enabled* = Yes, *Profile* = `PARTNER`, *Label override* = `Customer item`. The *Partner context* column shows **Customer**.
4. Optional: **Warehouse app field priority** — place *Item identity* directly under the Item field.

## Try it on USMF

1. Sign in to the Warehouse Management mobile app as user **24**.
2. Open **Sales Picking** and enter the work id of D6 (or scan the work).
3. Read the pick screen, confirm the pick, then read the put screen.

## Expected results

| Order | Worker sees | Why |
|---|---|---|
| D6 — US-001, W0001 Black/Large | `CR-4471-BL` | Exact-variant record for the customer |
| D6 — A0001 line (if added) | `A0001` | No record for A0001; plain item, so the variant line misses and line 30 resolves |
| D8 — US-003, W0001 Red/Small | `Wired Headset - Small / Red` | US-003 has no records; line 20 resolves |

## How it resolves

- The partner is the sales order's **customer account** (the same account standard order entry uses to put the external item number on the sales line). A separate invoice account on the order does not change it.
- Only sales work carries a customer; other work types skip the external source.
- The lookup follows the standard precedence — see [scenario 05](05-ExternalNumberPrecedence.md).

## Troubleshooting

| Symptom | Check |
|---|---|
| Variant name shows instead of `CR-4471-BL` | The external record's product dimensions do not match the picked variant, or the record is on a different customer account than the order's customer. Run [Test resolution](18-TestResolution.md) with account US-001. |
| No line at all | The setting row is keyed on the menu item **name** `Sales Picking`; the feature is enabled in **USMF** (configuration is per company). See the [smoke test](../ItemIdentitySmokeTest.md). |
| Old number still shows after a change | Cache — set the duration to 0 while testing, or edit the profile to invalidate. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 2. Vendor side: [scenario 02](02-VendorItemNumberOnReceiving.md).
