# Item Identity Scenario 03 — Telling Variants Apart

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

Several variants of one product master sit close together, and the item number alone does not tell the picker which one to take. The identity line spells out the variant in words.

## What the worker sees

```
Item:            W0001
Variant:         Wired Headset - Small / Red
Qty:             1 ea
```

## Supported flows

Sales picking (all picking styles), purchase order item/line receiving, load item receiving. The variant source needs no trading partner, so it also works on work without a partner ([scenario 15](15-WorkWithoutPartner.md)).

## Prerequisites

| Record | USMF value |
|---|---|
| Variant item | **W0001** *Wired Headset* — Color Black/Red × Size Small/Medium/Large, 10 each on hand in warehouse 24 (stock USMF data) |
| Sales order (D8) | Customer **US-003** *Forest Wholesales*, warehouse **24**, line W0001 **Red/Small** qty 1; released to warehouse |

**USMF caveat:** every W0001 variant's product name is *Wired Headset* — the same as the master (also true for M0030, D0010 and D0013). The variant name alone therefore cannot distinguish variants; the **dimension summary** does.

## Configure

1. **Item identity parameters**: enabled; *Fallback behavior* = Hide.
2. **Item identity profiles**: `VARIANT` — one line, 10 *Variant product name*, **Append dimension summary = Yes**.
3. **Item identity menu item settings**: `Sales Picking` → profile `VARIANT`, *Label override* = `Variant`.

## Try it on USMF

Sign in as user **24**, open **Sales Picking**, pick D8. Then switch *Append dimension summary* off on the profile line and re-open the pick screen.

## Expected results

| Item / variant | Append summary | Worker sees |
|---|---|---|
| W0001 Red/Small | Yes | `Wired Headset - Small / Red` |
| W0001 Red/Small | No | `Wired Headset` |
| W0001 Black/Large | Yes | `Wired Headset - Large / Black` |
| M0030 White/4 *(Test resolution)* | Yes | `Professional speaker cable - 4 / White` |
| D0010 Black *(Test resolution)* | Yes | `Active speaker - Black` |
| D0008 configuration Default *(Test resolution)* | Yes | `HighEndSpeaker 2 - Default` — the one USMF item whose variant has its own name |
| A0001 (no product dimensions) | — | no line: the source is skipped (*no product dimensions*) |

## How it resolves

- Name lookup: the distinct product variant's translation in the session language → the product master's translation in that language → the released product name ([scenario 11](11-MultiLanguage.md)).
- The summary lists dimension **values** (codes, not translated names) in the order configuration, size, color, style, version, separated by ` / `, after ` - `.
- An item without product dimensions skips this source; add a later line (for example *Item number*) if the line should never be empty.

## Troubleshooting

| Symptom | Check |
|---|---|
| Line shows only `Wired Headset` | *Append dimension summary* is off on the profile line. |
| Source reports *no product variant* in Test resolution | The dimension combination is not a released variant of the item. |
| Order of values looks reversed (`Small / Red`) | By design: size comes before color. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 1.
