# Item Identity Scenario 05 — External Number Precedence: Variant, Item, Group

Part of the [item identity use case catalog](../ItemIdentityUseCases.md). Worked on the Contoso **USMF** demo company.

## When to use

You maintain external item numbers at different levels: a number per variant, one generic number per item that covers all variants, and numbers shared by a group of customers (or vendors) through an external item description group. You need to know which one the worker sees.

## What the worker sees

One value — the winner of the standard four-level lookup:

```
Item identity:   CR-4471-BL     (exact variant, customer account)
Item identity:   RTL-4471-BL    (exact variant, customer item group)
Item identity:   CR-4471        (item level, customer account)
```

## Supported flows

Wherever a partner exists: sales picking (customer) and purchase order item/line and load receiving (vendor).

## Prerequisites

| Record | USMF value |
|---|---|
| D1 — exact variant, account | US-001, W0001 Black/Large → `CR-4471-BL` |
| D2 — item level, account | US-001, W0001, **no dimensions** → `CR-4471` |
| D4 — exact variant, group | External item description group `RETAIL` (**Sales and marketing > Setup > Items > External item description**), record for group `RETAIL`, W0001 Black/Large → `RTL-4471-BL`. Set *Item - customer group* = `RETAIL` on customer **US-002**. |
| Sales orders | D6 (US-001, W0001 Black/Large) and D7 (US-002, W0001 Black/Large), warehouse 24, released |

In USMF no customer or vendor has an external item description group out of the box; vendor group `01` exists but is unassigned.

## Configure

1. **Item identity parameters**: enabled; *Fallback behavior* = Hide; *Cache duration* = 0 while testing.
2. **Item identity profiles**: `EXTONLY` — one line, *External item number*.
3. **Item identity menu item settings**: `Sales Picking` → `EXTONLY`.

## Try it on USMF

Pick D6 and D7 with **Sales Picking** (user **24**), changing the records between renders as listed below.

## Expected results

| Order | Records present | Worker sees | Level that won |
|---|---|---|---|
| D6 (US-001) | D1, D2 | `CR-4471-BL` | 1 — exact variant, account |
| D6, with `RETAIL` also set on US-001, D1 deleted | D2, D4 | `RTL-4471-BL` | 2 — exact variant, group |
| D6, `RETAIL` removed from US-001 again | D2 | `CR-4471` | 3 — item level, account |
| D7 (US-002) | D4 | `RTL-4471-BL` | 2 — exact variant, group |
| D7 after adding an account record for US-002 | account record, D4 | the account record | 1 beats 2 |
| D8 (US-003) | none | no line | — |

Recreate D1 when done.

## How it resolves

The lookup is the standard one used by sales and purchase order entry:

1. exact product dimensions, partner **account**
2. exact product dimensions, partner's **external item description group**
3. item level (blank dimensions), partner account
4. item level, partner's group

Only the product dimensions active for the item are compared. The partner account is the order's customer (sales) or vendor (purchase) account — not the invoice account.

## Troubleshooting

| Symptom | Check |
|---|---|
| Group record never wins | The customer's *Item - customer group* is blank, or a level-1 record exists. |
| Item-level record ignored | A group record for the exact variant outranks it (level 2 beats level 3). |

## Related

Validation: [scenario pack](../ItemIdentityScenarios.md) scenario 2 (c)–(e).
