# Item Identity Scenario 10 — Show Several Identities at Once (Concatenate)

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

During a ramp-up, an audit, or for supervisors, the line should show every identity that exists — for example the customer's number, the variant, and the item number together.

## What the worker sees

```
Item identity:   CR-4471-BL | Wired Headset - Large / Black | W0001
```

## Supported flows

All supported flows. Partner-dependent lines simply drop out where there is no partner.

## Prerequisites

| Record | USMF value |
|---|---|
| D1 | US-001, W0001 Black/Large → `CR-4471-BL` |
| D6 | Sales order US-001, warehouse 24: W0001 Black/Large and A0001; released |
| D8 | Sales order US-003, warehouse 24: W0001 Red/Small; released |

## Configure

1. **Item identity profiles**: `COMBINED` — *Resolution mode* = **Concatenate**, *Separator* = ` | ` (the shipped default); lines 10 *External item number*, 20 *Variant product name* (Append dimension summary), 30 *Item number*.
2. **Item identity menu item settings**: `Sales Picking` → `COMBINED`.

## Try it on USMF

Pick D6 (both lines) and D8 with **Sales Picking** (user **24**).

## Expected results

| Pick | Worker sees |
|---|---|
| D6 W0001 Black/Large | `CR-4471-BL | Wired Headset - Large / Black | W0001` |
| D8 W0001 Red/Small | `Wired Headset - Small / Red | W0001` |
| D6 A0001 | `A0001` |
| D6 with separator ` / ` | `CR-4471-BL / Wired Headset - Large / Black / W0001` — ambiguous, see below |

## How it resolves

- Every line runs in sequence order; each value that resolves is appended with the separator. Lines that miss contribute nothing (no empty separators).
- The **first** contributing line is reported as the winning source (in Test resolution); the value is the full joined text.
- The fallback applies only when **no** line resolves.

## Troubleshooting

| Symptom | Check |
|---|---|
| Values are hard to tell apart | The dimension summary uses ` / ` and ` - ` internally; pick a separator that differs from both (the default ` | ` does). |
| Text wraps or is cut on the device | Keep concatenating profiles to two or three short sources. |
| The item number appears twice | A source repeated on two lines; the profile form warns about duplicate sources on save. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 16. First-hit version: [scenario 04](04-PartnerNumberWithFallbackChain.md).
