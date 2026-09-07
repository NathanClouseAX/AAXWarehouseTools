# AAX Warehouse Tools — Item Identity Display Smoke Test and Diagnostics

Use this when someone reports "the identity line does not show" (or shows the wrong thing). It is a deterministic 20-minute test that isolates the four layers the feature depends on — **deployment**, **configuration**, **injection**, and **resolution** — so a failure report names the layer, not just the symptom.

The trick: the smoke configuration uses a profile whose only source is **Item number** (which always resolves) with fallback **Item number** and caching off. With that setup the line *must* appear on a configured screen. If it does, injection works and any remaining problem is master data or profile design. If it does not, the problem is deployment or configuration, and the ladder in section 5 finds it.

---

## 1. Pre-flight checks

Run these in order. Each has a pass condition; do not continue past a failed check.

| # | Check | How to verify | If it fails |
|---|---|---|---|
| P1 | **Model deployed** | **Warehouse management > Setup > Mobile device** shows *Item identity parameters*, *Item identity profiles*, *Item identity menu item settings*. | The package is not deployed to this environment, or the navigation cache is stale — restart the AOS / redeploy. |
| P2 | **Database synchronized** | Open *Item identity parameters*; it opens and saves without an error. | A SQL error about a missing table or column means the sync did not run for this model — run a database synchronization. |
| P3 | **AOS restarted after deployment** | Form captions read as text ("Item identity parameters"), not as label ids ("@AAXWarehouseTools:…"). | Restart the AOS. This also loads the extension classes and refreshes the extension caches the mobile pipeline uses. |
| P4 | **Field registration visible** | Open **Warehouse app field priority** once. The field list contains **Item identity**. | The runtime has not loaded the new field class: restart the AOS (development box: also rebuild the model). The line can still render without this, but it proves the assembly is live. |
| P5 | **Correct legal entity** | Note the mobile worker's **default company** (Warehouse management > Setup > Worker > the user's *Default company*, or the company the device session shows). | All item identity configuration is **per legal entity**. Configure in the company the device session runs in — configuring in another company produces nothing. |
| P6 | **Exact menu item names** | On **Mobile device menu items**, write down the **Menu item name** (not the *Title* the device shows) of the picking and receiving items the tester uses. | Settings are keyed by *Menu item name*. A row keyed on the title, or on a similarly named item, matches nothing. |
| P7 | **Supported flow type** | The picking item is a **Work** menu item (user or system directed, cluster, grouping, pick and pack, work list); the receiving item is **Purchase order line receiving** or **Purchase order item receiving** (or the *and locate* variants). | Other menu item types (movement, counting, inquiries, adjustments) do not receive the line in this version — a setting row on them does nothing. |

## 2. Smoke configuration

Configure in the legal entity from P5.

**Item identity parameters**

| Field | Value |
|---|---|
| Enable item identity display | **Yes** |
| Default label | Item identity |
| Fallback behavior | **Item number** |
| Fallback text | (blank) |
| External item content | Identifier |
| Cache duration (minutes) | **0** (caching off while testing so every change shows immediately) |

**Item identity profiles** — create profile `SMOKE` (leave **Resolution mode** at *First hit*)

| Sequence | Source | External item content | Append dimension summary |
|---|---|---|---|
| 10 *(assigned automatically)* | Item number | Parameter default | No |

**Item identity menu item settings** — one row per menu item from P6

| Mobile device menu item | Enabled | Profile | Label override |
|---|---|---|---|
| *(exact picking menu item name)* | Yes | SMOKE | (blank) |
| *(exact PO line receiving menu item name)* | Yes | SMOKE | (blank) |

Do **not** configure field priority yet. Without a priority row the line renders in the default (details) area, which is enough to prove injection.

## 3. Execute

### Test A — Sales picking

1. Release a sales order for any item to the warehouse and wave it so picking work exists (any item — variants are not needed for the smoke test).
2. On the device, open the picking menu item from P6 and start the work.
3. Reach the **pick confirmation** page (item and quantity shown).

**Expected**: the page shows a read-only line **Item identity: `<item number>`**. On the Warehouse Management mobile app it renders among the item's detail fields — scroll or expand the item card if the screen is short. Continue to the **put** page: the line appears there too.

### Test B — Purchase order line receiving

1. Create and confirm a purchase order for any item.
2. On the device, open the PO line receiving menu item, scan the PO, then identify the item.
3. Reach the **quantity/confirmation** page.

**Expected**: the same **Item identity: `<item number>`** line among the item detail fields.

### Test C — Test resolution (rich client, no device)

1. **Item identity profiles** > select `SMOKE` > **Test resolution**.
2. Item = the item from Test A; leave Product dimension number blank; Partner context = None; Profile = SMOKE. Run.

**Expected** infolog:

```
Item number - resolved: <item number>
Winning source: Item number. Value: <item number>
```

## 4. Reading the results

| A (pick) | B (receive) | C (Test resolution) | Meaning | Go to |
|---|---|---|---|---|
| Pass | Pass | Pass | Deployment, configuration and injection all work. Any "wrong value" report is resolution/master data. | Section 6 |
| Fail | Fail | Pass | The engine works; injection is not reaching the page. | Section 5, ladder I |
| Pass | Fail | — | Picking injection works; receiving flow or its setting row is the issue. | Section 5, ladder I (steps I2, I4) |
| Fail | Pass | — | Receiving works; the picking menu item or its setting is the issue. | Section 5, ladder I (steps I2, I4) |
| Fail | Fail | Fail or error | Configuration or deployment. | Section 5, ladder C |
| — | — | Error / form errors | Deployment or sync. | Section 1 |

## 5. Diagnostic ladders

### Ladder C — configuration (Test resolution fails or errors)

| Step | Check | Fix |
|---|---|---|
| C1 | Test resolution reports *no source resolved* | The profile has no lines, or the lines all miss for this item. For `SMOKE` this means the profile lines grid is empty — add sequence 10 = Item number. |
| C2 | Test resolution throws | Capture the message; a missing-table error means the sync did not run (P2). |
| C3 | Profile not offered in the dialog | You are in a different legal entity than the one the profile was created in (P5). |

### Ladder I — injection (Test resolution passes, device shows nothing)

| Step | Check | Fix |
|---|---|---|
| I1 | **Parameters > Enable item identity display** is Yes *in the device session's legal entity* | Enable it in that company (P5). The parameters record is per company. |
| I2 | The setting row's **Mobile device menu item** equals the **Menu item name** (P6) of the item the tester actually opened, and **Enabled** is Yes | Fix the row. Titles on the device are not names. |
| I3 | The device page is one the feature covers: **pick / put** pages of a work menu item, or the **item/quantity** page of PO line or item receiving | Pages before the item is known (PO scan, work id scan, location scan) never carry the line by design; movement/counting flows are unsupported. |
| I4 | AOS restarted after the deployment (P3) | Restart — the injection classes are chain-of-command extensions loaded at startup. |
| I5 | Look in the **details area** of the item card on the app; scroll | Without a field-priority row the line sits at the end of the detail fields. Open **Warehouse app field priority** and give *Item identity* a priority directly under the Item field to bring it up. |
| I6 | The tester is on the same environment the configuration was made in | Confirm the app's connection (environment URL) and the company shown after login. |

### Ladder R — resolution (line shows the item number instead of the expected identity)

This is the fallback doing its job: the intended source missed. Reproduce with **Test resolution** using the same item, the variant's product dimension number, and the partner the flow supplies, then read the miss reason:

| Miss reason | Meaning | Fix |
|---|---|---|
| `no partner context` | External item number needs a customer or vendor; this flow supplies none | Expected on non-sales work and on movement-type flows. On sales picking, confirm the work belongs to a sales order. |
| `no partner account` | The order behind the work/receipt has no invoice account resolved | Check the sales/purchase order's invoice account. |
| `no external item record` | No external item description for *that* account (or its item group) and *those* product dimensions, nor an item-level one | Create the external item description on the customer/vendor, with the variant's dimensions or with blank dimensions. |
| `no product dimensions` | Variant product name skipped: the item is not a variant | Expected for plain items — the next source runs. |
| `no product variant` | Dimensions present but no released variant matches | Check the released product variants of the item. |
| `no GTIN record` / `no barcode record` | No registration for the variant nor at item level | Register the GTIN/barcode on the released product (variant-specific or item-level). |
| `empty search name` | Search name blank on the released product | Fill it or drop the source. |

If Test resolution resolves correctly but the device still shows the old/other value, caching is the reason — with **Cache duration** at 0 for the test this cannot occur; in production, edit the profile to force a refresh.

## 6. From smoke to the real profile

Once Tests A and B pass, switch the setting rows to the intended profile (for example External item number → Variant product name → Item number) and re-run Test A with a variant item and a customer that has an external item description. Use Test resolution with the same inputs first — its winning value is exactly what the device will show. Then set **Cache duration** back to its production value (30) and configure **Warehouse app field priority** for placement.

## 7. What to include in a failure report

- Environment URL, legal entity of the device session, and the deployed package/model version.
- The **Menu item name** (P6) and its type (work / PO line receiving / PO item receiving) — a screenshot of the Mobile device menu items record is ideal.
- Screenshots of **Item identity parameters**, the profile with its lines, and the menu item setting row, all taken in the device session's company.
- The device page where the line was expected (screenshot), including whether the details area was expanded.
- The **Test resolution** infolog for the same item, dimensions, partner and profile.
- The result table from section 4 (A / B / C pass or fail).
