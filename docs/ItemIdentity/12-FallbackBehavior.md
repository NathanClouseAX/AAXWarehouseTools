# Item Identity Scenario 12 — What Happens When Nothing Resolves

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

You must decide what the worker sees when no source in the profile returns a value: nothing, the item number, or a fixed message (for example to flag customers whose numbers are still missing).

## What the worker sees

```
Item identity:   NO CUSTOMER NO.      (Fixed text)
Item identity:   W0001                (Item number)
                                      (Hide — no line at all)
```

## Supported flows

All supported flows.

## Prerequisites

| Record | USMF value |
|---|---|
| D8 | Sales order for **US-003** *Forest Wholesales* (no external item descriptions), warehouse 24, W0001 Red/Small; released |

## Configure

1. **Item identity profiles**: `EXTONLY` — one line, *External item number*.
2. **Item identity menu item settings**: `Sales Picking` → `EXTONLY`.
3. **Item identity parameters** > *Fallback behavior*, one at a time: **Hide**, **Item number**, **Fixed text** with *Fallback text* = `NO CUSTOMER NO.`.

## Try it on USMF

Pick D8 with **Sales Picking** after each fallback change. The fallback is applied after the cache, so a parameter change shows on the next render.

## Expected results

| Fallback behavior | Worker sees |
|---|---|
| Hide | no identity line; the screen is identical to standard |
| Item number | `Item identity: W0001` |
| Fixed text | `Item identity: NO CUSTOMER NO.` |

## How it resolves

- The fallback runs only when **every** line misses (in both First hit and Concatenate mode).
- It is a parameter — one setting per company for all profiles. To have the item number as the last resort for one profile only, add an *Item number* line to that profile instead.
- A resolution error behaves like "nothing resolved" and never reaches the worker.

## Troubleshooting

| Symptom | Check |
|---|---|
| Fixed text shows but empty | *Fallback text* is blank. |
| Line never disappears with *Hide* | The profile's last line always resolves (for example *Item number*). |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenarios 5 and 10.
