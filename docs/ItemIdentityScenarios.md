# AAX Warehouse Tools — Item Identity Display Validation Scenarios

Fifteen scenario reviews covering the item identity feature end to end. Run them in order on a synchronized environment with the Warehouse Management mobile app connected; scenarios 1–4 build the master data the later ones reuse. Each scenario states its purpose, setup, steps, and expected outcome — record Pass/Fail and observations inline.

**Base data used throughout** (adjust names to your environment):

- A released product master `SHIRT` with the Color and Size dimensions active, variants including `Black/M`, and per-variant product name translations in at least two languages.
- A plain released product `A0001` with no product dimensions, a search name, and no external item numbers.
- Customer `C-100` (used on the test sales orders) with an external item description for `SHIRT` `Black/M` (external number `L102`), and a second customer `C-200` with no external records.
- Vendor `V-100` (used on the test purchase orders) with an external item number for `SHIRT` `Black/M`.
- Mobile device menu items: one user-directed **work execution** item (sales picking), one **purchase order line receiving** item, one **purchase order item receiving** item, one **movement** item.
- A profile `MAIN` with lines: 1 External item number, 2 Variant product name, 3 Item number.

---

## 1. Sales picking — variant name

**Purpose**: the variant's translated product name shows on the pick and put screens.

**Setup**: profile `VARIANT` with the single line *Variant product name*; assign it to the work execution menu item; parameters enabled, fallback *Hide*; a released sales order for `SHIRT` `Black/M` to customer `C-200`, waved to work.

**Steps**:
a. Execute the picking work on the device through the pick confirmation and the target license plate/put screens.
b. Change the worker's user language to the second translation language and repeat.
c. Delete the variant-level translation for that language (keep the product master's) and repeat.

**Expected**: (a) both screens show the identity line with the variant's product name; (b) the name appears in the session language; (c) the product master's translation appears instead. The line never appears as an editable field and never blocks confirmation.

**Result**: ☐ Pass ☐ Fail — notes:

## 2. Sales picking — external item number and its precedence

**Purpose**: the customer's number resolves with the standard precedence.

**Setup**: profile `MAIN` on the work execution menu item; sales order for `SHIRT` `Black/M` to customer `C-100`, waved to work.

**Steps**:
a. Pick the work; observe the line.
b. Set the parameter **External item content** to *Description*, then *Identifier and description*; touch the profile (any edit) to refresh the cache between changes; re-render the screen each time.
c. Add an item-group-level external record (customer item group on `C-100`) with a different number, alongside the account-level record; re-test. Then remove the account-level record and re-test.
d. Add an item-level external record (no dimensions) with a different number alongside the exact-variant record; re-test. Then remove the exact-variant record and re-test.

**Expected**: (a) `L102`; (b) the description, then `L102 - <description>`; (c) the account-level record wins while present, the group record after; (d) the exact-variant record wins while present, the item-level record after.

**Result**: ☐ Pass ☐ Fail — notes:

## 3. Purchase receiving — vendor context

**Purpose**: both PO receiving flows resolve with the vendor from the order.

**Setup**: profile `MAIN` on both PO receiving menu items; a purchase order for `SHIRT` `Black/M` from vendor `V-100`; a second PO from a vendor with no external records.

**Steps**: receive the first PO via **PO line receiving**, then via **PO item receiving**; observe the identity line on the quantity/confirmation screens. Repeat with the second PO.

**Expected**: the first PO shows `V-100`'s external number on both flows; the second shows the variant name (line 2 of the profile) — never another vendor's number.

**Result**: ☐ Pass ☐ Fail — notes:

## 4. Chain order — first hit wins

**Purpose**: profile sequence decides the winner.

**Setup**: scenario 2's data (external record exists for `C-100`).

**Steps**: on profile `MAIN`, swap lines 1 and 2 (Variant product name first); re-pick the same work; then swap back.

**Expected**: with the variant name first, the line shows the variant name even though the external record exists; restored order shows `L102` again. Each profile edit takes effect on the next screen render (version bump — no cache wait).

**Result**: ☐ Pass ☐ Fail — notes:

## 5. Fallback behavior

**Purpose**: all three fallbacks behave as configured when the whole chain misses.

**Setup**: profile `EXTONLY` with the single line *External item number*, assigned to the work execution menu item; sales work for customer `C-200` (no external records).

**Steps**: pick the work three times, once per parameter fallback: *Hide*, *Item number*, *Fixed text* (with a text set). Touch the profile between changes to skip the cache.

**Expected**: *Hide* — no identity line at all and the screen is otherwise identical to standard; *Item number* — the released product number; *Fixed text* — the configured text.

**Result**: ☐ Pass ☐ Fail — notes:

## 6. No-partner guard at setup

**Purpose**: the setup warning fires for a dead-end combination without blocking the save.

**Setup**: profile `EXTONLY` from scenario 5.

**Steps**: create a menu item setting for the **movement** menu item with profile `EXTONLY`; save.

**Expected**: a warning states the profile only contains partner-dependent sources while the menu item derives no partner context; the row saves anyway. At runtime the movement flow follows the fallback (and shows nothing extra on screens the feature does not cover).

**Result**: ☐ Pass ☐ Fail — notes:

## 7. Label defaulting and override

**Purpose**: caption precedence — override, then parameter default, then shipped default.

**Setup**: scenario 1's assignment.

**Steps**: render with (a) no override and the parameter **Default label** at its shipped value, (b) a changed parameter default, (c) a **Label override** on the menu item setting.

**Expected**: (a) *Item identity*; (b) the parameter text on this and every configured menu item without an override; (c) the override text on this menu item only.

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
a. Turn **Enable item identity display** off; run a full pick and a full PO line receive on configured menu items.
b. Turn it back on; run the same flows on an unconfigured menu item.
c. Inspect the four AAX tables and confirm no standard table carries AAX data.

**Expected**: (a) and (b) show no identity line and no behavior change of any kind — every confirmation, validation, and posting is standard; (c) configuration lives only in the AAX tables.

**Result**: ☐ Pass ☐ Fail — notes:

## 10. Exception isolation

**Purpose**: a resolution failure can never surface to the worker.

**Setup**: scenario 1's assignment; then corrupt the configuration deliberately — e.g. delete profile `VARIANT` while its menu item setting still references it (delete the lines first if the profile delete is restricted, leaving an empty profile; an empty profile is itself a valid "always miss" case, so also test with the setting pointing at a deleted/renamed profile id via DMF import if available).

**Steps**: execute the picking work.

**Expected**: the flow renders and completes normally — at worst the identity line is absent. No error message, no infolog on the device, no blocked step.

**Result**: ☐ Pass ☐ Fail — notes:

## 11. Caching and invalidation

**Purpose**: values are cached for the configured duration; profile edits invalidate immediately.

**Setup**: scenario 2's data; **Cache duration** 30.

**Steps**:
a. Pick to render the line once. Change the customer's external number in the master data; re-render within the cache window.
b. Edit the profile (any change); re-render.
c. Set **Cache duration** to 0; change the external number again; re-render.

**Expected**: (a) the old value still shows (cached); (b) the new value shows immediately (version bump); (c) with caching off, master-data changes show on the next render.

**Result**: ☐ Pass ☐ Fail — notes:

## 12. Test resolution parity

**Purpose**: the diagnostic shows exactly what the device shows.

**Setup**: scenario 2's data.

**Steps**: run **Test resolution** from profile `MAIN` with the same item, the variant's product dimension number, partner context *Customer*, account `C-100`. Compare with the device.

**Expected**: the trace lists each source in order with hit/miss and reasons; the winning source and value equal the device's line for the same inputs. Repeat with `C-200`: the external source reports its miss and the variant name wins — again matching the device.

**Result**: ☐ Pass ☐ Fail — notes:

## 13. Performance at scale

**Purpose**: no perceptible step delay on variant-heavy data.

**Setup**: an item population in the thousands of variants (a 10,000+ variant item if your test data has one), profile `MAIN`, caching at the default.

**Steps**: run repeated picking against varied variants; compare step response times with the menu item's setting row disabled versus enabled (toggling the row is a zero-deployment A/B switch).

**Expected**: no worker-perceptible difference; the first resolution per variant is a handful of indexed lookups, warm renders hit the cache.

**Result**: ☐ Pass ☐ Fail — notes:

## 14. Configuration round-trip via data management

**Purpose**: the four entities move the whole setup between environments.

**Steps**: export **Item identity parameters / profiles / profile lines / menu item settings** from the source environment; import into a clean target **with profiles sequenced before profile lines and menu item settings**; run scenario 2's pick on the target.

**Expected**: identical resolution behavior on the target; profile versions re-stamp safely (caches simply start cold).

**Result**: ☐ Pass ☐ Fail — notes:

## 15. Security

**Purpose**: setup is gated by the duty; workers need nothing new.

**Steps**:
a. As a user without the **Maintain item identity configuration** duty, try to open the three setup forms and Test resolution.
b. As a warehouse worker with unchanged standard roles, execute configured flows.
c. Grant the duty to the first user; retry (a).

**Expected**: (a) access denied; (b) flows work and the identity line shows — no elevation needed for display; (c) full setup access, nothing beyond it.

**Result**: ☐ Pass ☐ Fail — notes:

## 16. Concatenate mode

**Purpose**: a concatenating profile shows every resolving source, joined by the separator.

**Setup**: profile `MAIN` from the base data (External item number, Variant product name, Item number) with **Resolution mode** = *Concatenate* and the shipped separator; sales work for `SHIRT` `Black/M` to customer `C-100`.

**Steps**:
a. Pick the work; observe the line.
b. Change the separator to ` / ` and re-render (the profile edit refreshes the cache).
c. Repeat the pick for customer `C-200` (no external record).
d. Run **Test resolution** for the same inputs.

**Expected**: (a) `L102 | <variant name> | SHIRT`; (b) the same values joined with ` / `; (c) the external source is simply absent: `<variant name> | SHIRT`; (d) the trace shows one *resolved* line per contributing source and reports the first contributor as the winning source, with the full concatenated value.

**Result**: ☐ Pass ☐ Fail — notes:

## 17. Sequence defaulting and duplicate-source warning

**Purpose**: new lines number themselves; repeating a source warns without blocking.

**Steps**:
a. On a new profile, add a line without touching **Sequence**; add two more.
b. Delete the middle line and add another.
c. Set the new line's **Source** to one already used on another line; save.

**Expected**: (a) sequences 10, 20, 30 assigned automatically; (b) the new line gets 40 (highest plus ten — gaps are not reused); (c) a warning names the source and the sequence that already uses it, and the line still saves.

**Result**: ☐ Pass ☐ Fail — notes:

---

## Review summary

| # | Scenario | Result |
|---|---|---|
| 1 | Sales picking — variant name | |
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

Reviewed by: _______________  Date: _______________  Build: _______________
