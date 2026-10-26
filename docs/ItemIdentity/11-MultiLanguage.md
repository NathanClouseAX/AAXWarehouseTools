# Item Identity Scenario 11 — Workers in Another Language

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

Workers read the device in a language other than the company's product language, and product names should appear in their language.

## What the worker sees

```
Item:            W0001
Variant:         Casque filaire rouge S - Small / Red
```

## Supported flows

All supported flows; applies to the **Variant product name** source only. External numbers, GTINs, barcodes, search names and item numbers are not translated.

## Prerequisites

USMF's W0001 has **English only** translations. Add, for the test:

| Record | Where | Value |
|---|---|---|
| Variant translation | **Product information management > Products > Product variants** > W0001 Red/Small > **Translations** | Language `fr`, name `Casque filaire rouge S` |
| Master translation | **Products** > W0001 > **Translations** | Language `fr`, name `Casque filaire` |
| Session language | The user the mobile app session signs in as: **User options > Preferences > Language** | `fr` |
| Sales order D8 | US-003, warehouse 24, W0001 Red/Small; released | |

## Configure

1. **Item identity profiles**: `VARIANT` — 10 *Variant product name*, Append dimension summary = Yes.
2. **Item identity menu item settings**: `Sales Picking` → `VARIANT`.
3. *Cache duration* = 0 while testing (the language is part of the cache key, but master-data edits are not).

## Try it on USMF

Pick D8 with **Sales Picking**, removing translations between renders as below.

## Expected results

| Translations present | Worker sees |
|---|---|
| Variant `fr` + master `fr` | `Casque filaire rouge S - Small / Red` |
| Master `fr` only | `Casque filaire - Small / Red` |
| Neither | `Wired Headset - Small / Red` (released product name) |
| Session language `en-us` | `Wired Headset - Small / Red` |

## How it resolves

1. The distinct product variant's translation in the session language.
2. The product master's translation in the session language.
3. The released product name.

Dimension values in the summary are codes (`Small`, `Red`) and are never translated. The caption (label override / default label) is plain text and is not translated either — use a label override per menu item if crews differ.

## Troubleshooting

| Symptom | Check |
|---|---|
| English name although a translation exists | The session's language — the mobile app session runs as a specific user; check that user's language, not the worker record. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 1 (c)–(e).
