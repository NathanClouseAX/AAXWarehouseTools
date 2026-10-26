# Item Identity Scenario 04 — Partner Number When It Exists, Variant Otherwise

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

Only some customers and vendors send their own item numbers. The worker should always see the most useful identity available: the partner's number where one exists, the variant in words where it does not, and at least the item number. This is the **recommended starting profile** for most sites.

## What the worker sees

The same caption, different values depending on the order:

```
Item identity:   CR-4471-BL                       (US-001 has a number)
Item identity:   Wired Headset - Small / Red      (US-003 has none)
Item identity:   A0001                            (plain item, no number)
```

## Supported flows

All supported flows: sales picking (every picking style), purchase order item/line receiving, load item receiving. On work without a partner the first line is skipped and the chain continues ([scenario 15](15-WorkWithoutPartner.md)).

## Prerequisites

| Record | USMF value |
|---|---|
| Customer external item description (D1) | US-001, W0001 Black/Large → `CR-4471-BL` |
| Sales order (D6) | US-001, warehouse 24: W0001 Black/Large qty 2 **and** A0001 qty 1; released |
| Sales order (D8) | US-003, warehouse 24: W0001 Red/Small qty 1; released |

## Configure

1. **Item identity parameters**: enabled; *Fallback behavior* = Hide (the chain's last line always resolves, so the fallback never triggers).
2. **Item identity profiles**: `PARTNER`, *Resolution mode* = First hit:

   | Sequence | Source | Options |
   |---|---|---|
   | 10 | External item number | — |
   | 20 | Variant product name | Append dimension summary = Yes |
   | 30 | Item number | — |

3. **Item identity menu item settings**: `Sales Picking` and `Purchase Receive` → profile `PARTNER`.

## Try it on USMF

Sign in as user **24**; pick D6 (both lines) and D8 with **Sales Picking**.

## Expected results

| Pick | Line 10 | Line 20 | Line 30 | Worker sees |
|---|---|---|---|---|
| D6 W0001 Black/Large | `CR-4471-BL` | *(not evaluated)* | *(not evaluated)* | `CR-4471-BL` |
| D6 A0001 | no external item record | no product dimensions | `A0001` | `A0001` |
| D8 W0001 Red/Small | no external item record | `Wired Headset - Small / Red` | *(not evaluated)* | `Wired Headset - Small / Red` |

## How it resolves

- In *First hit* mode the lines run in **Sequence** order and the first one that returns a value wins; later lines are not evaluated.
- Reordering lines changes the winner immediately — every profile edit bumps the profile version, which invalidates cached values.
- Drop line 30 and set the fallback to *Hide* if you prefer the line to disappear rather than repeat the item number.

## Troubleshooting

| Symptom | Check |
|---|---|
| Always shows the variant name, even for US-001 | Line order — the variant line may have a lower sequence than the external line. |
| A0001 shows nothing | Line 30 missing and fallback *Hide*. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenarios 4 and 12. Show all of them at once instead: [scenario 10](10-ConcatenatedIdentity.md).
