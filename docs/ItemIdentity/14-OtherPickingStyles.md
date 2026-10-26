# Item Identity Scenario 14 — System-Directed, Cluster and Grouped Picking

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

Your warehouse picks sales work with a style other than user-directed picking: system directed, cluster picking, or grouping (pick to container). The identity line works the same on all of them.

## What the worker sees

```
Cluster Pick
Item:            W0001
Color / Size:    Black / Large
Item identity:   CR-4471-BL
```

## Supported flows

| USMF menu item | Directed by | Work classes | Partner context column |
|---|---|---|---|
| Sales Picking | User directed | SO Pick, Sales | Customer |
| Sales Picking - System | System directed | SO Pick, Sales | Customer |
| Cluster Pick | Cluster picking | Sales | Customer |
| Cluster Pick Create | Cluster picking | Sales | Customer |
| Pick to Container | System grouping | Sales | Customer |
| Sales Loading | User grouping | Sales, SO Load | Customer |

The line appears on every **pick** and **put** screen these menu items render. Pick and pack menu items behave the same.

## Prerequisites

D1 and a released sales order like D6 (US-001, warehouse 24, W0001 Black/Large) for each style you test — cluster picking needs work that is not already in progress on another menu item.

## Configure

**Item identity menu item settings**: one row per menu item above, each with profile `PARTNER`. The *Partner context* column shows **Customer** for each, because each lists a sales work class.

## Try it on USMF

Sign in as user **24** and run the work with each menu item. For **Cluster Pick**, build the cluster from the D6-like work, then pick.

## Expected results

| Menu item | Worker sees for US-001, W0001 Black/Large |
|---|---|
| Sales Picking - System | `CR-4471-BL` |
| Cluster Pick | `CR-4471-BL` |
| Pick to Container | `CR-4471-BL` |

## How it resolves

All of these execute work through the standard pick and put screen builders, which is where the line is added. Each work line resolves with its own item, variant and — for sales work — its own order's customer, so a cluster mixing orders for different customers shows each customer's own number.

## Troubleshooting

| Symptom | Check |
|---|---|
| Line missing on one style only | That menu item has no settings row of its own — settings are per menu item, not per flow. |
| No customer number on a mixed menu item | The work actually picked is not sales work (see [scenario 15](15-WorkWithoutPartner.md)). |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 20 (a)–(b).
