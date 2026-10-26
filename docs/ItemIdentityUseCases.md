# AAX Warehouse Tools — Item Identity Display Use Cases (USMF)

Every supported scenario of the item identity line, worked against the Contoso **USMF** demo company so each one has real items, partners, warehouses and menu items to try. This page holds the shared sample data and configuration; each scenario has its own step-by-step guide (section 3). For the setup reference see the [setup guide](ItemIdentitySetup.md); for a scripted review pack see the [validation scenarios](ItemIdentityScenarios.md).

## 1. What USMF gives you, and what to add

USMF is a good base for picking and receiving, but its identity master data is thin. Know this before you test, or most sources simply miss.

### Already in USMF

| Data | Values | Notes |
|---|---|---|
| Warehouse | **24** — *Site 2 - Warehouse Management* (site 2) | The standard WHS demo warehouse. Mobile user **24** (*WH 24*) defaults to it. Warehouse **63** (*Site 6 - Demo wave containerization*, user **63**) also works. |
| Variant item | **W0001** *Wired Headset* — Color × Size: Black/Red × Small/Medium/Large; 10 each on hand in warehouse 24 | The workhorse for variant use cases. |
| Variant items without stock | **M0030** *Professional speaker cable* (Black/White × 2/4/6/8), **D0010** *Active speaker* (Black/White), **D0013** *SpecialSpeaker* (S/M/L), **D0008** *Licensed High End Speaker* (configuration *Default*) | Use with **Test resolution** (no device needed), or add on-hand first. |
| Plain items | **A0001** *HDMI 6' Cables* (search name `HDMI 6' Cables`), **A0002** *HDMI 12' Cables*, **SW0001** *Wireless Headset* (search name `WirelessHeadset`) | A0001/A0002 are stocked in warehouses 24 and 63. |
| Item barcodes | **D0013**: EAN-13 `0012345678912` (S), `0012345678929` (M), `0012345678936` (L), all scanning-enabled | Present on this environment's USMF data; create them if your copy lacks them. |
| Customers | **US-001** *Contoso Retail San Diego*, **US-002** *Contoso Retail Los Angeles*, **US-003** *Forest Wholesales*, **US-027** *Birch Company* | None has a customer item group or a separate invoice account. |
| Vendors | **US-101** *Fabrikam Electronics*, **US-104** *Fabrikam Supplier* | None has a vendor item group. |
| Mobile menu items | **Sales Picking** (user directed), **Sales Picking - System** (system directed), **Cluster Pick**, **Pick to Container**, **Purchase Receive** (purchase order *item* receiving), **Purchase Put-away**, **Transfer pick**, **Movement** | There is **no** purchase order *line* receiving and **no** load item receiving menu item — create them to test those flows (section 4). |

### What USMF lacks — and why it matters

- **Variant names equal the master name.** Every W0001 variant's product name is *Wired Headset* (the same holds for M0030, D0010 and D0013). The *Variant product name* source therefore resolves, but cannot tell Black/Large from Red/Small on its own — turn on **Append dimension summary** on that line. Only **D0008** has a distinct variant name (*HighEndSpeaker 2* versus master *Licensed High End Speaker*).
- **Translations are English only** for these items. Multi-language use cases need a translation added ([scenario 11](ItemIdentity/11-MultiLanguage.md)).
- **No GTINs** are registered in USMF.
- **No external item descriptions** ship for these customers and vendors.

### Sample data to create

Create these once in USMF; the use cases below refer to them. All external item descriptions are maintained on the released product (**Product information management > Released products > W0001 > Sell / Purchase > External item description**) or the customer/vendor.

| # | Record | Values |
|---|---|---|
| D1 | Customer external item description | Customer **US-001**, item **W0001**, color **Black**, size **Large** → external item number `CR-4471-BL`, description `Contoso Retail headset black L` |
| D2 | Customer external item description (item level) | Customer **US-001**, item **W0001**, no dimensions → `CR-4471`, description `Contoso Retail headset` |
| D3 | Vendor external item description | Vendor **US-101**, item **W0001**, Black / Large → `FAB-HS200-BK-L`, description `Fabrikam HS-200 headset black large` |
| D4 | Customer item group | Group `RETAIL` (*Contoso retail stores*); external item description for the **group** on W0001 Black / Large → `RTL-4471-BL`. Assign the group to **US-002** (customer > *Sales order defaults* > *Customer item group*). |
| D5 | GTINs | W0001 Black/Large `0614141000104`, Black/Medium `0614141000111`, Black/Small `0614141000128`, Red/Large `0614141000135`, Red/Medium `0614141000142`, Red/Small `0614141000159`; A0001 item level `0614141000203` (all unit blank) |
| D6 | Sales order | Customer **US-001**, site 2, warehouse **24**, line W0001 Black/Large qty 2 and A0001 qty 1. Release to warehouse so picking work exists. |
| D7 | Sales order | Customer **US-002**, warehouse 24, line W0001 Black/Large qty 1; released. |
| D8 | Sales order | Customer **US-003**, warehouse 24, line W0001 Red/Small qty 1; released. |
| D9 | Purchase order | Vendor **US-101**, warehouse 24, line W0001 Black/Large qty 5; confirmed. |
| D10 | Purchase order | Vendor **US-104**, warehouse 24, line W0001 Black/Large qty 5; confirmed. |

The GTINs use the GS1 example company prefix `0614141` with valid check digits — test values only.

## 2. Configuration used below

**Item identity parameters** (USMF): enabled, default label *Item identity*, fallback **Hide**, external item content **Identifier**, cache **0** while testing (set it back to 30 afterwards).

| Profile | Mode | Lines (sequence: source, options) |
|---|---|---|
| `PARTNER` | First hit | 10 External item number · 20 Variant product name *(Append dimension summary)* · 30 Item number |
| `VARIANT` | First hit | 10 Variant product name *(Append dimension summary)* |
| `EXTONLY` | First hit | 10 External item number |
| `GTIN` | First hit | 10 GTIN · 20 Item number |
| `BARCODE` | First hit | 10 Item barcode |
| `SEARCH` | First hit | 10 Search name |
| `COMBINED` | Concatenate (separator ` | `) | 10 External item number · 20 Variant product name *(Append dimension summary)* · 30 Item number |

Assign profiles on **Item identity menu item settings**, keyed by the menu item **name** (in USMF the names and titles are identical, e.g. `Sales Picking`).

## 3. Supported scenarios

Each supported scenario has its own guide — when to use it, the USMF records it needs, configuration, steps on the device, the exact expected values, how it resolves, and troubleshooting.

| # | Scenario | Flows | Worker sees on USMF |
|---|---|---|---|
| 01 | [Customer item number on sales picking](ItemIdentity/01-CustomerItemNumberOnPicking.md) | Sales picking | `CR-4471-BL` |
| 02 | [Vendor item number on purchase receiving](ItemIdentity/02-VendorItemNumberOnReceiving.md) | Purchase Receive | `FAB-HS200-BK-L` |
| 03 | [Telling variants apart](ItemIdentity/03-VariantIdentification.md) | All | `Wired Headset - Small / Red` |
| 04 | [Partner number when it exists, variant otherwise](ItemIdentity/04-PartnerNumberWithFallbackChain.md) — the recommended profile | All | `CR-4471-BL` / `Wired Headset - Small / Red` / `A0001` |
| 05 | [External number precedence: variant, item, group](ItemIdentity/05-ExternalNumberPrecedence.md) | Picking, receiving | `CR-4471-BL` / `RTL-4471-BL` / `CR-4471` |
| 06 | [External description instead of (or with) the number](ItemIdentity/06-ExternalItemDescription.md) | Picking, receiving | `CR-4471-BL - Contoso Retail headset black L` |
| 07 | [Show the GTIN](ItemIdentity/07-GTIN.md) | All | `0614141000104` |
| 08 | [Show an item barcode](ItemIdentity/08-ItemBarcode.md) | All | `0012345678929` |
| 09 | [Show the search name](ItemIdentity/09-SearchName.md) | All | `HDMI 6' Cables` |
| 10 | [Several identities at once (concatenate)](ItemIdentity/10-ConcatenatedIdentity.md) | All | `CR-4471-BL | Wired Headset - Large / Black | W0001` |
| 11 | [Workers in another language](ItemIdentity/11-MultiLanguage.md) | All | `Casque filaire rouge S - Small / Red` |
| 12 | [What happens when nothing resolves (fallback)](ItemIdentity/12-FallbackBehavior.md) | All | no line / `W0001` / `NO CUSTOMER NO.` |
| 13 | [Different captions per flow](ItemIdentity/13-CaptionsPerFlow.md) | All | *Customer item* / *Vendor item* |
| 14 | [System-directed, cluster and grouped picking](ItemIdentity/14-OtherPickingStyles.md) | Sales Picking - System, Cluster Pick, Pick to Container | `CR-4471-BL` |
| 15 | [Put-away, transfer, production and replenishment work](ItemIdentity/15-WorkWithoutPartner.md) | Purchase Put-away, Transfer pick, Production pick, Replenishment | `Wired Headset - Large / Black` |
| 16 | [Purchase order line receiving](ItemIdentity/16-PurchaseOrderLineReceiving.md) | PO Line Receive *(create)* | `FAB-HS200-BK-L` |
| 17 | [Load item receiving](ItemIdentity/17-LoadItemReceiving.md) | Load Receive *(create)* | `FAB-HS200-BK-L` |
| 18 | [Check a value without a device (Test resolution)](ItemIdentity/18-TestResolution.md) | Rich client | the device's value, with a per-source trace |
## 4. Where the line does not appear

| Flow (USMF menu item) | Behavior |
|---|---|
| **Movement**, **Movement by template**, adjustments, **Status change** | No line. A setting row is accepted but does nothing. |
| **Cycle Count**, **Spot Counting** | No line. |
| **Transfer receive**, **Transfer receive LP**, **GIT receive** | No line (transfer receiving is not covered). |
| **RMA Receive**, **RMA and Putaway** | No line (return receiving is not covered). |
| **Kanban receipt**, **RAFand putaway**, **Report serial number** | No line. |
| Inquiries, license plate build/break, packing | No line. |
| Screens before the item is known (PO scan, work id scan, location scan) | No line by design — it appears once the item is identified. |

## 5. Quick reference — expected values on USMF

| Scenario | Menu item | Order / item | Profile | Worker sees |
|---|---|---|---|---|
| [01](ItemIdentity/01-CustomerItemNumberOnPicking.md) | Sales Picking | D6 · W0001 Black/Large | PARTNER | `CR-4471-BL` |
| [02](ItemIdentity/02-VendorItemNumberOnReceiving.md) | Purchase Receive | D9 · W0001 Black/Large | PARTNER | `FAB-HS200-BK-L` |
| [02](ItemIdentity/02-VendorItemNumberOnReceiving.md) | Purchase Receive | D10 · W0001 Black/Large | PARTNER | `Wired Headset - Large / Black` |
| [03](ItemIdentity/03-VariantIdentification.md) | Sales Picking | D8 · W0001 Red/Small | VARIANT | `Wired Headset - Small / Red` |
| [04](ItemIdentity/04-PartnerNumberWithFallbackChain.md) | Sales Picking | D6 · A0001 | PARTNER | `A0001` |
| [05](ItemIdentity/05-ExternalNumberPrecedence.md) | Sales Picking | D7 · W0001 Black/Large | EXTONLY | `RTL-4471-BL` |
| [07](ItemIdentity/07-GTIN.md) | Sales Picking | D6 · W0001 Black/Large | GTIN | `0614141000104` |
| [09](ItemIdentity/09-SearchName.md) | Sales Picking | D6 · A0001 | SEARCH | `HDMI 6' Cables` |
| [10](ItemIdentity/10-ConcatenatedIdentity.md) | Sales Picking | D6 · W0001 Black/Large | COMBINED | `CR-4471-BL | Wired Headset - Large / Black | W0001` |
| [15](ItemIdentity/15-WorkWithoutPartner.md) | Purchase Put-away | D9 receipt | PARTNER | `Wired Headset - Large / Black` |
