# AAX Warehouse Tools — Item Identity Display Validation Scenarios

Twenty-two scenario reviews covering the item identity feature end to end, written against the Contoso **USMF** demo company. Run them in order on a synchronized environment with the Warehouse Management mobile app connected, logged on as mobile user **24** (warehouse 24). Each scenario states its purpose, setup, steps, and expected outcome — record Pass/Fail and observations inline. This pack is the sign-off script; for the explanation behind each behavior, see the per-scenario guides indexed in the [use case catalog](ItemIdentityUseCases.md).

**Base data used throughout** — stock USMF plus the sample records D1–D10 from the [use case catalog](ItemIdentityUseCases.md#sample-data-to-create). In short:

- Item **W0001** *Wired Headset* (Color Black/Red × Size Small/Medium/Large, 10 of each on hand in warehouse 24). Every variant's product name is *Wired Headset*, in English only.
- Item **A0001** *HDMI 6' Cables* — no product dimensions, search name `HDMI 6' Cables`, stocked in warehouse 24.
- Customer **US-001** *Contoso Retail San Diego* with external item descriptions for W0001: Black/Large `CR-4471-BL` (D1) and item level `CR-4471` (D2).
- Customer **US-002** *Contoso Retail Los Angeles* in customer item group `RETAIL`, which has `RTL-4471-BL` for W0001 Black/Large (D4).
- Customer **US-003** *Forest Wholesales* with no external records.
- Vendor **US-101** *Fabrikam Electronics* with `FAB-HS200-BK-L` for W0001 Black/Large (D3); vendor **US-104** *Fabrikam Supplier* with none.
- GTINs for the six W0001 variants and A0001 (D5).
- Released sales orders D6 (US-001: W0001 Black/Large ×2, A0001 ×1), D7 (US-002: W0001 Black/Large), D8 (US-003: W0001 Red/Small); confirmed purchase orders D9 (US-101) and D10 (US-104), both W0001 Black/Large to warehouse 24.
- Mobile device menu items: **Sales Picking** (user directed work), **Purchase Receive** (purchase order item receiving), **Movement**, **Cluster Pick**, **Purchase Put-away**, plus two you create: **PO Line Receive** (purchase order line receiving) and **Load Receive** (load item receiving).
- Profile `MAIN`: 10 External item number · 20 Variant product name with **Append dimension summary** · 30 Item number.
- Parameters: enabled, fallback **Hide**, external item content **Identifier**, cache **30** unless a scenario says otherwise.

---

## 1. Sales picking — variant name and language fallback

**Purpose**: the variant's translated product name shows on the pick and put screens, with the documented language fallback.

**Setup**: profile `VARIANT` with the single line *Variant product name* (Append dimension summary on); assign it to **Sales Picking**; cache 0.

**Steps**:
a. Pick D8 (US-003, W0001 Red/Small) through the pick screen and the put screen.
b. Turn **Append dimension summary** off on the line; re-render.
c. Turn it back on. Add a French (`fr`) translation `Casque filaire rouge S` to the W0001 Red/Small **variant**; set the mobile session user's language to `fr`; re-render.
d. Delete the variant's French translation and add `Casque filaire` to the W0001 **product master**; re-render.
e. Delete the master's French translation; re-render.

**Expected**: (a) both screens show `Wired Headset - Small / Red`; (b) `Wired Headset` — correct but indistinguishable from the other five variants, which is why USMF needs the summary; (c) `Casque filaire rouge S - Small / Red`; (d) `Casque filaire - Small / Red`; (e) `Wired Headset - Small / Red` (released product name). Dimension codes are never translated. The line is never editable and never blocks confirmation.

**Result**: ☐ Pass ☐ Fail — notes:

## 2. Sales picking — external item number and its precedence

**Purpose**: the customer's number resolves with the standard four-level precedence.

**Setup**: profile `MAIN` on **Sales Picking**; cache 0.

**Steps**:
a. Pick D6 (US-001, W0001 Black/Large); observe the line.
b. Set the parameter **External item content** to *Description*, then *Identifier and description*; re-render each time. Set it back to *Identifier*.
c. Assign customer item group `RETAIL` to US-001 as well. Re-render. Delete D1 (US-001's exact-variant record); re-render. Remove `RETAIL` from US-001 again; re-render.
d. Pick D7 (US-002, group `RETAIL` only).
e. Recreate D1. Create a sales order like D6 for US-001 but set its **Invoice account** to US-003; release and pick it.

**Expected**: (a) `CR-4471-BL`; (b) `Contoso Retail headset black L`, then `CR-4471-BL - Contoso Retail headset black L`; (c) `CR-4471-BL` (exact variant for the account wins), then `RTL-4471-BL` (exact variant for the group beats the account's item-level record), then `CR-4471` (item level for the account); (d) `RTL-4471-BL`; (e) `CR-4471-BL` — the order's customer account decides, not the invoice account, matching the external item number standard order entry puts on the sales line.

**Result**: ☐ Pass ☐ Fail — notes:

## 3. Purchase receiving — vendor context

**Purpose**: both purchase order receiving flows resolve with the vendor from the order.

**Setup**: profile `MAIN` on **Purchase Receive** and on **PO Line Receive**.

**Steps**: receive part of D9 (US-101) with **Purchase Receive** (scan PO, item W0001, Black, Large) and the rest with **PO Line Receive**; observe the identity line on the quantity/confirmation screens. Repeat with D10 (US-104).

**Expected**: D9 shows `FAB-HS200-BK-L` on both flows; D10 shows `Wired Headset - Large / Black` (line 20 of the profile) — never another vendor's number.

**Result**: ☐ Pass ☐ Fail — notes:

## 4. Chain order — first hit wins

**Purpose**: profile sequence decides the winner.

**Setup**: scenario 2's data (D1 exists for US-001).

**Steps**: on profile `MAIN`, give the Variant product name line sequence 5 so it runs first; re-pick D6; then set it back to 20.

**Expected**: with the variant name first, the line shows `Wired Headset - Large / Black` even though `CR-4471-BL` exists; the restored order shows `CR-4471-BL` again. Each profile edit takes effect on the next screen render (version bump — no cache wait).

**Result**: ☐ Pass ☐ Fail — notes:

## 5. Fallback behavior

**Purpose**: all three fallbacks behave as configured when the whole chain misses.

**Setup**: profile `EXTONLY` with the single line *External item number*, assigned to **Sales Picking**; pick D8 (US-003, no external records).

**Steps**: render the pick screen three times, once per parameter fallback: *Hide*, *Item number*, *Fixed text* with text `NO CUSTOMER NO.`. Touch the profile between changes to skip the cache.

**Expected**: *Hide* — no identity line at all and the screen is otherwise identical to standard; *Item number* — `W0001`; *Fixed text* — `NO CUSTOMER NO.`.

**Result**: ☐ Pass ☐ Fail — notes:

## 6. No-partner guard at setup

**Purpose**: the setup warning fires for a dead-end combination without blocking the save, and the derived partner context matches what the device does.

**Setup**: profile `EXTONLY` from scenario 5.

**Steps**:
a. Create a menu item setting for **Movement** with profile `EXTONLY`; save.
b. Create settings with `EXTONLY` for **Cluster Pick** and **Load Receive**; save each.
c. Create a setting with `EXTONLY` for **Purchase Put-away**; save.

**Expected**: (a) a warning states the profile only contains partner-dependent sources while the menu item derives no partner context; the row saves anyway; the movement flow shows nothing (unsupported flow). (b) no warning; the **Partner context** column shows *Customer* for **Cluster Pick** and *Vendor* for **Load Receive** — both supply that partner at runtime (scenarios 20 and 21). (c) the warning appears and the column shows *None* — purchase put-away work resolves without a partner (scenario 20). Delete these rows afterwards.

**Result**: ☐ Pass ☐ Fail — notes:

## 7. Label defaulting and override

**Purpose**: caption precedence — override, then parameter default, then shipped default.

**Setup**: scenario 1's assignment.

**Steps**: render with (a) no override and the parameter **Default label** at its shipped value, (b) the parameter default changed to *Identity*, (c) a **Label override** *Variant* on the **Sales Picking** setting.

**Expected**: (a) *Item identity*; (b) *Identity* on this and every configured menu item without an override; (c) *Variant* on **Sales Picking** only.

**Result**: ☐ Pass ☐ Fail — notes:

## 8. Field priority and promotion

**Purpose**: placement and prominence are pure standard configuration.

**Setup**: any working scenario, e.g. scenario 1.

**Steps**:
a. Open **Warehouse app field priority** once; confirm the **Item identity** field is listed.
b. With no priority assigned to it, render the screen and note where the line lands.
c. Assign it to the priority group right below the Item field; re-render.
d. Promote the field for the pick step via step instructions/promoted fields; re-render.

**Expected**: (a) the field self-registers on first open; (b) the line renders in the default/secondary info area; (c) it renders directly under the item; (d) it renders promoted per the standard feature.

**Result**: ☐ Pass ☐ Fail — notes:

## 9. Non-intrusion

**Purpose**: disabled or unconfigured means byte-identical standard behavior.

**Steps**:
a. Turn **Enable item identity display** off; run a full pick of D6 and a full **Purchase Receive** of D9 on configured menu items.
b. Turn it back on; run the same flows on an unconfigured menu item (for example **Sales Picking - System**).
c. Inspect the four AAX tables and confirm no standard table carries AAX data.

**Expected**: (a) and (b) show no identity line and no behavior change of any kind — every confirmation, validation, and posting is standard; (c) configuration lives only in the AAX tables.

**Result**: ☐ Pass ☐ Fail — notes:

## 10. Exception isolation

**Purpose**: a resolution failure can never surface to the worker.

**Setup**: scenario 1's assignment; then break the configuration deliberately — point the **Sales Picking** setting at a profile id that does not exist (via a data management import of the menu item settings entity), or delete all lines of profile `VARIANT` (an empty profile is a valid "always miss" case).

**Steps**: pick D8.

**Expected**: the flow renders and completes normally — at worst the identity line is absent. No error message, no infolog on the device, no blocked step.

**Result**: ☐ Pass ☐ Fail — notes:

## 11. Caching and invalidation

**Purpose**: values are cached for the configured duration; profile edits invalidate immediately.

**Setup**: scenario 2's data; **Cache duration** 30.

**Steps**:
a. Pick D6 to render `CR-4471-BL` once. Change D1's external item number to `CR-4471-BLK`; re-render within the cache window.
b. Edit the profile (any change); re-render.
c. Set **Cache duration** to 0; change the number back to `CR-4471-BL`; re-render.

**Expected**: (a) `CR-4471-BL` still shows (cached); (b) `CR-4471-BLK` shows immediately (version bump); (c) with caching off, `CR-4471-BL` shows on the next render. The cache is per AOS: on a multi-AOS environment another AOS may still serve its own copy until it expires.

**Result**: ☐ Pass ☐ Fail — notes:

## 12. Test resolution parity

**Purpose**: the diagnostic shows exactly what the device shows.

**Setup**: scenario 2's data.

**Steps**: run **Test resolution** from profile `MAIN` with item `W0001`, the Black/Large variant's product dimension number, partner context *Customer*, account `US-001`. Compare with the device for D6. Repeat with account `US-003`.

**Expected**: US-001: `External item number - resolved: CR-4471-BL`, winning source External item number. US-003: the external source reports *no external item record* and `Variant product name - resolved: Wired Headset - Large / Black` wins — matching the device in both cases.

**Result**: ☐ Pass ☐ Fail — notes:

## 13. Performance at scale

**Purpose**: no perceptible step delay on variant-heavy data.

**Setup**: USMF has no large variant population (W0001 with 6 and M0030 with 8 variants are the largest WHS-enabled masters); for a real scale test, generate a master with thousands of variants and on-hand, or run in a customer copy. Profile `MAIN`, caching at the default.

**Steps**: run repeated picking against varied variants; compare step response times with the menu item's setting row disabled versus enabled (toggling the row is a zero-deployment A/B switch).

**Expected**: no worker-perceptible difference; the first resolution per variant is a handful of indexed lookups, warm renders hit the cache. Cached values are keyed per item, product dimensions (variant), partner, profile version and language, so the same variant picked from different warehouses, locations or license plates resolves once and is then served from the cache.

**Result**: ☐ Pass ☐ Fail — notes:

## 14. Configuration round-trip via data management

**Purpose**: the four entities move the whole setup between environments.

**Steps**: export **Item identity parameters / profiles / profile lines / menu item settings** from USMF; import into a clean target (another USMF copy) **with profiles sequenced before profile lines and menu item settings**; create the D1 record there and run scenario 2(a) on the target.

**Expected**: identical resolution behavior on the target; profile versions re-stamp safely (caches simply start cold).

**Result**: ☐ Pass ☐ Fail — notes:

## 15. Security

**Purpose**: setup is gated by the duty; workers need nothing new.

**Steps**:
a. As a user without the **Maintain item identity configuration** duty, try to open the three setup forms and Test resolution.
b. As mobile user **24** with unchanged standard roles, execute configured flows.
c. Grant the duty to the first user; retry (a).

**Expected**: (a) access denied; (b) flows work and the identity line shows — no elevation needed for display; (c) full setup access, nothing beyond it.

**Result**: ☐ Pass ☐ Fail — notes:

## 16. Concatenate mode

**Purpose**: a concatenating profile shows every resolving source, joined by the separator.

**Setup**: profile `MAIN` with **Resolution mode** = *Concatenate* and the shipped separator ` | `.

**Steps**:
a. Pick D6 (US-001, W0001 Black/Large); observe the line.
b. Change the separator to ` / ` and re-render (the profile edit refreshes the cache).
c. Pick D8 (US-003, W0001 Red/Small).
d. Pick the A0001 line of D6.
e. Run **Test resolution** for the inputs of (a).

**Expected**: (a) `CR-4471-BL | Wired Headset - Large / Black | W0001`; (b) `CR-4471-BL / Wired Headset - Large / Black / W0001` — note that ` / ` collides with the dimension summary's own separator, so pick a separator that does not; (c) `Wired Headset - Small / Red | W0001`; (d) `A0001`; (e) one *resolved* trace line per contributing source, the first contributor reported as the winning source, with the full concatenated value.

**Result**: ☐ Pass ☐ Fail — notes:

## 17. Sequence defaulting and duplicate-source warning

**Purpose**: new lines number themselves; repeating a source warns without blocking.

**Steps**:
a. On a new profile, add a line without touching **Sequence**; add two more.
b. Delete the middle line and add another.
c. Set the new line's **Source** to one already used on another line; save.

**Expected**: (a) sequences 10, 20, 30 assigned automatically; (b) the new line gets 40 (highest plus ten — gaps are not reused); (c) a warning names the source and the sequence that already uses it, and the line still saves.

**Result**: ☐ Pass ☐ Fail — notes:

## 18. GTIN and item barcode sources

**Purpose**: GTIN and barcode lookups match the variant, fall back to item level, and prefer the right registration.

**Setup**: profile `GTIN` (10 GTIN · 20 Item number) on **Sales Picking**; cache 0. Profile `BARCODE` (10 Item barcode) for Test resolution.

**Steps**:
a. Pick D6: the W0001 Black/Large line, then the A0001 line.
b. Add a second GTIN for W0001 Black/Large with a unit (for example the item's inventory unit) and a different number; re-render.
c. Delete the Black/Large GTIN(s) and add an item-level (no dimensions) GTIN `0614141000210` for W0001; re-render.
d. Test resolution with profile `BARCODE`, item `D0013`, once per size (S, M, L inventory dimensions).
e. On one D0013 barcode, clear **Use for input** (scanning) and add a second, scanning-enabled barcode for the same size; repeat (d) for that size.

**Expected**: (a) `0614141000104`, then `0614141000203`; (b) still `0614141000104` (unit-blank wins); (c) `0614141000210` (item-level fallback); (d) S `0012345678912`, M `0012345678929`, L `0012345678936`; (e) the scanning-enabled barcode wins.

**Result**: ☐ Pass ☐ Fail — notes:

## 19. Search name and item number sources

**Purpose**: the plain released-product sources.

**Setup**: profile `SEARCH` (10 Search name) on **Sales Picking**, fallback *Hide*.

**Steps**:
a. Pick D6 (both lines).
b. Clear the search name of A0001 temporarily; re-render the A0001 line.
c. Add line 20 *Item number* to `SEARCH`; re-render.

**Expected**: (a) W0001 → `WiredHeadset`, A0001 → `HDMI 6' Cables`; (b) no line; (c) `A0001`. Restore A0001's search name.

**Result**: ☐ Pass ☐ Fail — notes:

## 20. Other picking styles and work without a partner

**Purpose**: the line appears on every work-execution pick/put screen; only sales work supplies a partner.

**Setup**: profile `MAIN` on **Sales Picking - System**, **Cluster Pick** and **Purchase Put-away**.

**Steps**:
a. Pick D6's work with **Sales Picking - System**.
b. Create a new sales order like D6, release it, and pick it with **Cluster Pick**.
c. Put away the license plate received from D9 in scenario 3 with **Purchase Put-away**.

**Expected**: (a) and (b) `CR-4471-BL` on the pick and put screens; (c) `Wired Headset - Large / Black` — purchase work carries no partner, so the Fabrikam number is not shown (the external line reports *no partner context* in Test resolution with context *None*).

**Result**: ☐ Pass ☐ Fail — notes:

## 21. Load item receiving

**Purpose**: load receiving resolves with the purchase order's vendor.

**Setup**: profile `MAIN` on **Load Receive**; a new purchase order like D9 (US-101, W0001 Black/Large) with an inbound load created for it.

**Steps**: receive the load with **Load Receive**; observe the quantity/confirmation screen.

**Expected**: `FAB-HS200-BK-L`.

**Result**: ☐ Pass ☐ Fail — notes:

## 22. Unsupported flows stay standard

**Purpose**: menu items outside the supported flows never show the line, even when configured.

**Setup**: profile `SMOKE` (10 Item number — always resolves) assigned to **Movement**, **Transfer receive**, **RMA Receive** and **Spot Counting**.

**Steps**: run each flow on a W0001 or A0001 item in warehouse 24.

**Expected**: no identity line on any screen, and no behavior change. Delete the setting rows afterwards.

**Result**: ☐ Pass ☐ Fail — notes:

---

## Review summary

| # | Scenario | Result |
|---|---|---|
| 1 | Sales picking — variant name and language fallback | |
| 2 | External item number precedence | |
| 3 | Purchase receiving — vendor context | |
| 4 | Chain order | |
| 5 | Fallback behavior | |
| 6 | No-partner guard | |
| 7 | Label defaulting and override | |
| 8 | Field priority and promotion | |
| 9 | Non-intrusion | |
| 10 | Exception isolation | |
| 11 | Caching and invalidation | |
| 12 | Test resolution parity | |
| 13 | Performance at scale | |
| 14 | Configuration round-trip | |
| 15 | Security | |
| 16 | Concatenate mode | |
| 17 | Sequence defaulting and duplicate-source warning | |
| 18 | GTIN and item barcode sources | |
| 19 | Search name and item number sources | |
| 20 | Other picking styles and work without a partner | |
| 21 | Load item receiving | |
| 22 | Unsupported flows stay standard | |

Reviewed by: _______________  Date: _______________  Build: _______________
