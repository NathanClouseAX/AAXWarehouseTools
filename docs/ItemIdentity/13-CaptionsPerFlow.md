# Item Identity Scenario 13 — Different Captions per Flow

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

The same profile serves several flows, but the caption should say what the value is in that flow — *Customer item* when picking, *Vendor item* when receiving.

## What the worker sees

```
Sales Picking:      Customer item:   CR-4471-BL
Purchase Receive:   Vendor item:     FAB-HS200-BK-L
Cluster Pick:       Item identity:   CR-4471-BL
```

## Supported flows

All supported flows.

## Prerequisites

D1, D3, D6 and D9 from the [sample data](../ItemIdentityUseCases.md#sample-data-to-create).

## Configure

1. **Item identity parameters** > *Default label* = `Item identity` (shipped value).
2. **Item identity menu item settings**, all with profile `PARTNER`:

   | Mobile device menu item | Label override |
   |---|---|
   | `Sales Picking` | `Customer item` |
   | `Purchase Receive` | `Vendor item` |
   | `Cluster Pick` | *(blank)* |

## Try it on USMF

Pick D6 with **Sales Picking** and with **Cluster Pick**; receive D9 with **Purchase Receive**. Then change the parameter *Default label* to `Identity` and repeat on **Cluster Pick**.

## Expected results

| Menu item | Caption |
|---|---|
| Sales Picking | `Customer item` |
| Purchase Receive | `Vendor item` |
| Cluster Pick | `Item identity`, then `Identity` after the parameter change |
| Any configured item, parameter *Default label* blank | `Item identity` (shipped default) |

## How it resolves

Caption precedence: the setting's **Label override** → the parameter's **Default label** → the shipped text *Item identity*. The caption does not affect the cache; changes show on the next screen.

## Troubleshooting

| Symptom | Check |
|---|---|
| One menu item shows an unexpected caption | That row has a label override. |
| Captions do not change language with the worker | Captions are plain text, not labels — set the override in the language the crew reads. |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 7.
