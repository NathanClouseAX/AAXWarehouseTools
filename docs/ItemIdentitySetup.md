# AAX Warehouse Tools — Item Identity Display Setup Guide

This guide is for the implementer configuring the **item identity line**: a single read-only line on Warehouse Management mobile app screens that shows the identity your warehouse actually works with — a customer or vendor external item number, the variant's product name, a GTIN, an item barcode, or the search name — instead of only the released product number.

The feature is entirely configuration-driven and dormant until configured. With the feature disabled, or on menu items without a setting, the app renders standard pages with no change.

## 1. Prerequisites

- The AAXWarehouseTools model is deployed and the database has been synchronized (four new tables are created by sync).
- After deployment, restart the AOS once so the new label texts load.
- Your user has the **Maintain item identity configuration** duty (see [Security](#7-security)) or system administrator rights.

> **First deployment or "nothing shows"?** Follow the [smoke test and diagnostics](ItemIdentitySmokeTest.md) — a fixed configuration that proves the line renders before you design real profiles, with a diagnostic ladder for each failure layer. Note that all configuration is **per legal entity**: configure in the company the device session runs in.

## 2. Setup at a glance

1. Turn the feature on and choose defaults — **Item identity parameters**.
2. Define one or more resolution profiles — **Item identity profiles**.
3. Assign a profile to each mobile device menu item that should show the line — **Item identity menu item settings**.
4. Place the line on the screen — standard **Warehouse app field priority** (and optionally promoted fields).

All three setup forms are under **Warehouse management > Setup > Mobile device**.

## 3. Item identity parameters

| Field | Meaning |
|---|---|
| **Enable item identity display** | Master switch. Off = every mobile screen is standard, byte for byte. |
| **Default label** | Caption of the identity line on the device. Ships as *Item identity*; each menu item can override it. |
| **Fallback behavior** | What the line does when no source in the profile resolves: **Hide** (default — the line is simply absent), **Item number** (show the released product number), or **Fixed text**. |
| **Fallback text** | The text shown when Fallback behavior is *Fixed text*. |
| **External item content** | What an external item number resolves to: **Identifier** (default), **Description**, or **Identifier and description**. Profile lines can override this per source. |
| **Cache duration (minutes)** | How long a resolved value is reused before it is looked up again. Default 30. `0` disables caching (every screen render resolves fresh — not recommended on busy sites). |

## 4. Item identity profiles

A profile is an ordered list of identity sources. At render time the resolver walks the lines in sequence; what happens on a hit depends on the profile's **Resolution mode**:

- **First hit** (default) — the first source that returns a value supplies the line; later lines are skipped.
- **Concatenate** — every source that returns a value contributes, and the values are joined with the profile's **Separator** (ships as ` | `), for example `L102 | Golf Shirt - Black / M`. The fallback still applies only when *no* source resolves.

New lines get their **Sequence** automatically (the highest existing sequence plus ten, so lines can be inserted between existing ones); overtype it to reorder. Using the same source on two lines raises a warning on save — it is allowed, but a repeated source never adds a different value.

| Source | Returns | Notes |
|---|---|---|
| **External item number** | The trading partner's item number for this item and variant | Resolved with partner context: the customer on sales picking, the vendor on purchase receiving. Follows the standard precedence — an account-specific record beats an item-group record, and an exact variant match beats an item-level record. Skipped entirely on flows without a partner. |
| **Variant product name** | The distinct product variant's product name, translated to the worker's language | Falls back to the product master's translation, then the released product name. Skipped when the item has no product dimensions (the master name would duplicate the item line). |
| **GTIN** | The GTIN registered for the item and variant | A unit-blank registration is preferred over unit-specific ones. |
| **Item barcode** | A barcode registered for the item and variant | A scanning-enabled barcode is preferred. |
| **Search name** | The released product's search name | Skipped when empty. |
| **Item number** | The released product number | Always resolves — use it as the last line when you want the line always filled. |

Per line you can also set:

- **External item content** — override the parameter default for this profile's external item number line.
- **Append dimension summary** — suffix the active product dimension values to the resolved value, for example `L102 - Black / M`.

**Profile version** is stamped automatically: any edit to the profile or its lines bumps it, which immediately invalidates cached values for that profile. You never maintain it by hand.

### Recommended starting profile

| Sequence | Source |
|---|---|
| 1 | External item number |
| 2 | Variant product name |
| 3 | Item number |

The partner-specific number is the most operative identity when it exists; variant-only sites still get the variant name because the external lookup simply misses; the item number guarantees the line is never empty. Pair it with Fallback behavior *Hide* if you prefer the line to disappear instead when nothing better than the item number exists — then drop line 3.

## 5. Item identity menu item settings

Assign profiles to mobile device menu items either from **Item identity menu item settings** directly, or from **Mobile device menu items** via the **Item identity** button (which opens the settings filtered to the selected menu item).

| Column | Meaning |
|---|---|
| **Mobile device menu item** | The menu item the setting applies to. One setting per menu item. |
| **Enabled** | Turns the line on or off for this menu item without deleting the row. |
| **Profile** | The resolution profile to use. |
| **Label override** | Menu-item-specific caption; blank uses the parameter default. |
| **Partner context** | Read-only, derived from the menu item's flow: **Vendor** for purchase order line/item receiving, **Customer** for work execution (picking), **None** otherwise. |

If you assign a profile whose only source is *External item number* to a menu item with no partner context, the form warns you — that combination can never resolve and the line will always follow the fallback. The save still goes through.

### Where the line can appear

The line is injected into these screens when their menu item has an enabled setting:

- **Work execution** (user directed, system directed, cluster picking, grouping, pick and pack, work list) — the **pick** and **put** screens, including the screens that carry the target license plate prompt. Partner context is the **customer** from the sales order's invoice account when the work is sales work; other work types resolve without a partner (the external item number source is skipped, everything else works).
- **Purchase order line receiving** and **purchase order item receiving** (including the *and locate* variants) — the item/quantity confirmation screens, with the **vendor** from the purchase order's invoice account.
- **Load item receiving** — with the vendor when the load line traces to a purchase order.

Configuring any other menu item type (movement, counting, inquiries, …) has no effect in this version.

## 6. Placing the line — Warehouse app field priority

The identity line participates in the standard **Warehouse app field priority** mechanism.

1. Open **Warehouse management > Setup > Mobile device > Warehouse app field priority**. Opening the form registers the **Item identity** field automatically — no extra step.
2. Find **Item identity** in the field list and assign it to the priority group directly below the Item field (its shipped default priority already places it near the item description).
3. To make the line prominent on a specific step (large font, top of screen), promote it with the standard **step instructions / promoted fields** feature — no AAX setup involved.

## 7. Security

| Element | Grants |
|---|---|
| Duty **Maintain item identity configuration** | The three setup forms, the Test resolution action, and data management access to the four entities below. Assign it to your warehouse-manager-class roles. |
| Privilege **Maintain item identity configuration** | Included in the duty; use it directly for finer-grained role design. |
| Privileges **…EntityMaintain** (four) | Data management (DMF) access per entity, included in the duty. |

Warehouse workers need **no additional access** — resolution and display ride on the worker's existing mobile-flow permissions.

## 8. Moving configuration between environments

Four data management entities carry the whole setup:

| Entity | Contents |
|---|---|
| **Item identity parameters** | The parameter record |
| **Item identity profiles** | Profile headers |
| **Item identity profile lines** | The ordered source lines |
| **Item identity menu item settings** | The menu item assignments |

Import order matters: **profiles before profile lines and before menu item settings** (both reference the profile). Parameters can load any time. Profile versions are re-stamped on import; caches on the target start cold.

## 9. Verifying a profile — Test resolution

On **Item identity profiles**, the **Test resolution** action resolves a sample request without a device and lists every source in order with its outcome:

- Enter an **Item number**, optionally a **Product dimension number** (an inventory dimension number carrying the variant's product dimensions), a **Partner context** and **Account**, and the **Profile** (defaulted from the selected profile). The **Account** lookup follows the partner context — it lists customers for *Customer*, vendors for *Vendor*, and the field is disabled while the context is *None*.
- The output shows one line per source — resolved value or the miss reason — followed by the winning source and value. This is exactly what the device would show, except Test resolution always bypasses the cache.

## 10. Behavior guarantees

- **Non-intrusive**: feature off or menu item unconfigured means unchanged standard screens; no standard table gets AAX data.
- **Never blocks the flow**: the line is read-only, is ignored by input-completeness logic, and every resolution error is swallowed — the worst case is an absent line.
- **Caching**: resolved values are cached per item, variant, partner, profile version, and language for the configured duration. Editing a profile takes effect immediately (the version bump invalidates the cache); master-data changes (a new GTIN, a new external item number) appear when the cache entry expires — or immediately after you touch the profile.
