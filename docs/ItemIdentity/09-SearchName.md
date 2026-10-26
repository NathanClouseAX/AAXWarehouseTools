# Item Identity Scenario 09 — Show the Search Name

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

The floor knows products by their short names, and the search name on the released product carries exactly that.

## What the worker sees

```
Item:            A0001
Short name:      HDMI 6' Cables
```

## Supported flows

All supported flows. No partner is needed.

## Prerequisites

Stock USMF data — no records to create:

| Item | Search name |
|---|---|
| A0001 | `HDMI 6' Cables` |
| A0002 | `HDMI 12' Cables` |
| W0001 | `WiredHeadset` |
| SW0001 | `WirelessHeadset` |
| M9212 | `Speakerdriver` |

Sales order D6 (US-001, warehouse 24: W0001 Black/Large and A0001), released.

## Configure

1. **Item identity profiles**: `SEARCH` — 10 *Search name* (optionally 20 *Item number*).
2. **Item identity menu item settings**: `Sales Picking` → `SEARCH`, *Label override* = `Short name`.

## Try it on USMF

Pick both lines of D6 with **Sales Picking** (user **24**).

## Expected results

| Pick | Worker sees |
|---|---|
| W0001 (any variant) | `WiredHeadset` |
| A0001 | `HDMI 6' Cables` |
| A0001 with its search name cleared | no line (fallback *Hide*), or `A0001` with line 20 |

## How it resolves

The released product's **Search name** field, unchanged. It is item-level: every variant of W0001 shows the same value — combine with the dimension summary on another line, or use [scenario 03](03-VariantIdentification.md), when variants matter.

## Troubleshooting

| Symptom | Check |
|---|---|
| No line | Search name empty; Test resolution reports *empty search name*. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 19.
