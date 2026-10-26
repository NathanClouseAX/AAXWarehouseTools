# Item Identity Scenario 18 — Check a Value Without a Device (Test Resolution)

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

Before rolling a profile out, or when a worker reports an unexpected value, replay the exact resolution from the rich client and see why each source hit or missed.

## What you see

```
External item number - no value (no external item record)
Variant product name - resolved: Wired Headset - Small / Red
Winning source: Variant product name. Value: Wired Headset - Small / Red
```

When nothing resolves: *No source resolved; the device line would follow the fallback behavior.*

## Prerequisites

The **Maintain item identity configuration** duty. Any profile.

## Run it

1. **Warehouse management > Setup > Mobile device > Item identity profiles**, select the profile, **Test resolution**.
2. Fill the dialog:

   | Field | What to enter |
   |---|---|
   | Item number | The item, e.g. `W0001` |
   | Product dimension number | An inventory dimension id carrying the variant's product dimensions (blank for item level / plain items) |
   | Partner context | *Customer* to mimic sales picking, *Vendor* for receiving, *None* for other work |
   | Account | The customer or vendor (lookup follows the context; disabled for *None*) |
   | Profile | Defaults to the selected profile |

3. Run; the infolog lists every line in sequence with its outcome.

**Finding the product dimension number:** look up the variant's inventory dimension, for example from the on-hand list for the variant. On the reference USMF database: W0001 Black/Large `#000000100001A91A`, Black/Medium `#000000100001A91B`, Black/Small `#000000100001A91C`, Red/Large `#000000100001A917`, Red/Medium `#000000100001A918`, Red/Small `#000000100001A919`; D0013 S `000852`, M `000850`, L `000851`. Ids differ between environments.

## Expected results on USMF (profile `PARTNER`)

| Item / dimension | Context / account | Winning source | Value |
|---|---|---|---|
| W0001 Black/Large | Customer / US-001 | External item number | `CR-4471-BL` |
| W0001 Red/Small | Customer / US-003 | Variant product name | `Wired Headset - Small / Red` |
| W0001 Black/Large | Vendor / US-101 | External item number | `FAB-HS200-BK-L` |
| W0001 Black/Large | None | Variant product name | `Wired Headset - Large / Black` (external: *no partner context*) |
| A0001 (blank) | Customer / US-001 | Item number | `A0001` |

## Miss reasons

| Reason | Meaning |
|---|---|
| no partner context | External item number on a flow without a partner |
| no partner account | Context set but no account |
| no external item record | No record at any of the four precedence levels |
| empty external item content | Record found, but the selected content (e.g. description) is empty |
| no product dimensions | Variant name skipped for a plain item or blank dimension number |
| no product variant | Dimensions do not match a released variant |
| no product name | No translation and no released product name |
| no GTIN record / no barcode record | No registration for the variant nor at item level |
| empty search name | Search name blank |
| source lookup failed | An error inside that source; treated as a miss |

## How it relates to the device

Test resolution runs the same resolver the device uses, but **always bypasses the cache**. If Test resolution and the device disagree, the device is serving a cached value — edit the profile or wait out the cache duration. The fallback and the caption are applied on the device only.

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 12. Device-side diagnostics: [smoke test](../ItemIdentitySmokeTest.md).
