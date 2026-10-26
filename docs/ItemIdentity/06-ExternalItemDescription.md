# Item Identity Scenario 06 — External Description Instead of (or With) the Number

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

A partner's numbers are cryptic, but its short descriptions are what the floor recognises — or you want both side by side.

## What the worker sees

```
Customer item:   Contoso Retail headset black L                  (Description)
Customer item:   CR-4471-BL - Contoso Retail headset black L     (Identifier and description)
```

## Supported flows

Same as the external item number: sales picking and purchase order item/line and load receiving.

## Prerequisites

| Record | USMF value |
|---|---|
| D1 | US-001, W0001 Black/Large → number `CR-4471-BL`, description `Contoso Retail headset black L` |
| D6 | Sales order for US-001, warehouse 24, W0001 Black/Large; released |

## Configure

The content option exists on two levels:

| Where | Field | Effect |
|---|---|---|
| **Item identity parameters** | *External item content* | Default for every profile line left at *Parameter default*. Ships as *Identifier*. |
| **Item identity profiles** > line | *External item content* | Overrides the parameter for that line only. |

Values: **Identifier**, **Description**, **Identifier and description**.

1. Profile `PARTNER` (10 External item number · 20 Variant product name with summary · 30 Item number) on `Sales Picking`, label override `Customer item`.
2. Set line 10's *External item content* to *Description*; then to *Identifier and description*.

## Try it on USMF

Pick D6 with **Sales Picking** (user **24**) after each change. Profile edits take effect on the next screen; a **parameter** change needs the cache to expire or any profile edit.

## Expected results

| Content option | Worker sees |
|---|---|
| Identifier | `CR-4471-BL` |
| Description | `Contoso Retail headset black L` |
| Identifier and description | `CR-4471-BL - Contoso Retail headset black L` |
| Identifier and description, record has no description | `CR-4471-BL` |
| Description, record has no description | the line misses (*empty external item content*) and line 20 resolves |

## How it resolves

The record is found with the normal precedence ([scenario 05](05-ExternalNumberPrecedence.md)); the option only decides which of its two fields is displayed. With *Identifier and description*, a missing part is dropped rather than leaving a dangling ` - `.

## Troubleshooting

| Symptom | Check |
|---|---|
| Changed the parameter but the device still shows the old content | Cache — the parameter is not part of the cache key. Edit the profile, or wait out the cache duration. |
| Long descriptions are cut or wrap | Device display width; prefer *Identifier* or keep descriptions short. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 2 (b).
