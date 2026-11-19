# AAX Warehouse Tools

On-demand ZPL label preview, printer-free mobile-flow label capture, an item identity line on the warehouse app, and blind PO receiving for Microsoft Dynamics 365 Finance & Operations (Warehouse management) — all label rendering happens inside the AOS, with no label data leaving your environment.

## Overview

AAX Warehouse Tools adds these capabilities to Warehouse management:

- **Generate a label preview on demand** — see exactly what a ZPL layout will produce, as a PNG image, without sending anything to a printer or to any external service.
- **Capture labels printed from mobile/RF flows and attach them to Work** — when enabled, ZPL labels that are printed from warehouse mobile flows and that belong to a Work record are rendered to images and attached to that Work, even when no physical printer is configured.
- **Show an item identity line on the warehouse app** — a configurable read-only line on picking and receiving screens that shows the identity the warehouse actually works with: the customer's or vendor's external item number, the variant's product name, a GTIN, an item barcode, or the search name.
- **Receive freight that arrives without a purchase order** — the lead opens an empty purchase order for each unannounced arrival, and the warehouse worker builds its lines on the mobile device from what is actually delivered.

Both label features render ZPL to images entirely in-process. The renderer is a single self-contained assembly (`AtomicAx.Zpl.Render.dll`) that ships in the package `bin` folder; there is nothing else to install.

## Features

### Feature A — On-demand label preview

A **Generate label preview** action button is added to the action pane of the **Label layout** and **Document routing layout** forms. Selecting it opens the **Label preview** dialog, which:

- Renders the layout's ZPL to one PNG image per label (`^XA … ^XZ` block).
- Resolves `$tokens$` and template blocks the same way they are resolved at print time, using a live seed record when one is available and falling back to generated **sample data** when it is not. A badge indicates whether the preview used a **Live record** or **Sample data**.
- Lets you choose the seed record from a **Data source** group and re-render with **Refresh preview**.
- Lets you **edit the source ZPL** (with its `$tokens$` / template blocks) in an editable panel, preview the edits without saving, and **Save to layout** to write the edited ZPL back to the active layout version. A read-only panel shows the translated (token-substituted) ZPL that is actually rendered.
- Supports an optional **size override** — turn on **Override label size** and set **Width (in)**, **Height (in)**, and **DPI** to force the render size instead of the size the label declares with `^PW` / `^LL`.
- Supports **Rotate 90°**, **Previous label** / **Next label** paging for multi-label jobs (with a "Label *n* of *m*" caption), **Copy ZPL**, and **Download PNG**.

This feature applies to label layouts whose definition type is **ZPL** (including layouts that use template blocks). Layouts whose definition type is **Variables** or **VariablesScript** are not previewable and the dialog reports this with a clear message.

### Feature B — Mobile-flow label capture to Work attachment

When **Print label preview to work attachment** is enabled, ZPL labels that are printed from warehouse mobile flows and that have a related Work record are captured at the document-routing print path, rendered to images, and attached to that Work record. Because the capture happens at the print path rather than at a printer, it works even when **no printer is configured** for the routing.

Rendering and attachment happen in near-real-time immediately after the label is built. A scheduled batch task acts as a fallback so that any label not rendered in-line is still picked up. The finished image appears on the Work record's attachments (the standard paperclip / Document handling), as document type **Label preview**.

Capture is designed not to stay on indefinitely: it automatically turns itself off after a configurable number of days, and captured attachments are purged after a configurable retention period.

### Feature C — Item identity line on the warehouse app

Warehouse Management mobile screens identify goods by the released product number. When the operative identity is something else — the variant, a trading partner's item number, a GTIN — workers cannot confirm they have the right goods without leaving the flow. Feature C injects **one configurable, read-only item identity line** into picking and receiving screens, resolved through an ordered chain of identity sources with partner context (the customer on sales picking, the vendor on purchase receiving). Placement and prominence use the standard **Warehouse app field priority** and promoted-fields mechanisms.

The feature is dormant until configured, adds nothing to standard tables, and can never block a warehouse transaction — a resolution failure simply means the line is absent. See the [Item identity setup guide](docs/ItemIdentitySetup.md), the [Item identity user guide](docs/ItemIdentityUserGuide.md), the [use case catalog](docs/ItemIdentityUseCases.md) (a step-by-step guide for each of the 18 supported scenarios, worked on Contoso USMF data), the [smoke test and diagnostics](docs/ItemIdentitySmokeTest.md), and the [validation scenarios](docs/ItemIdentityScenarios.md).

### Feature D — Blind PO receiving

Freight sometimes arrives at the warehouse with no purchase order or advance notice. Standard mobile receiving needs purchase order lines to receive against, so this stock can't be received. Feature D creates the purchase order on demand and builds the receipt from what is delivered:

1. For each arrival, the lead creates an empty purchase order in the warehouse's **blind receipt pool**.
2. On the mobile device, the worker scans a license plate and that purchase order number, then identifies each item as it comes off the truck.
3. Each confirmed item adds a zero-price line to the order and puts the quantity on the license plate. **LP Complete** registers the plate and creates its put-away work.
4. A nightly batch, **Post blind receipts**, posts the product receipt for what was registered. Workers can still add lines to the same order afterwards.

The flow runs on a standard **Mixed license plate receiving** menu item with its own identification method, **Blind PO receiving**, so standard receiving menu items are untouched. It is switched on per warehouse. The device refuses items whose primary vendor isn't the order's vendor, orders outside the warehouse's blind receipt pool, and a second order on the same license plate. Each device-added line records the mobile worker who added it and when it was registered. Cancelling the put-away work can unregister the receipt and delete the lines the device added.

See the [Blind PO receiving setup guide](docs/BlindReceivingSetup.md) and the [Blind PO receiving user guide](docs/BlindReceivingUserGuide.md). The feature was tracked in [issue #7](https://github.com/NathanClouseAX/AAXWarehouseTools/issues/7).

## Prerequisites

- Microsoft Dynamics 365 Finance & Operations with the **Warehouse management** module enabled.
- Access to deploy a package into the environment and to run a database synchronization (a standard developer or admin task in a non-production environment, or a deployable package in production).
- System administrator (or equivalent) rights to configure Warehouse management parameters, Document routing, batch tasks, and security roles.

No printer is required for any of these features.

## Installation and database synchronization

1. **Deploy the `AAXWarehouseTools` package.** The package holds two models that deploy together: `AAXWarehouseTools` (Features A–C) and `AAXBlindReceiving` (Feature D). The ZPL renderer ships as a single self-contained assembly, `AtomicAx.Zpl.Render.dll`, which is already present in the package `bin` folder. No separate native-library deployment is required.
2. **Synchronize the database.** This adds the Warehouse parameters fields used by the label-capture feature, the label-capture staging table that buffers labels between capture and rendering, the item identity configuration tables, and the blind receiving fields on warehouses and purchase lines.

After the package is deployed and the database is synchronized, all features are available; label capture, item identity and blind PO receiving still need to be enabled and configured (below).

## Configuration

### Warehouse management parameters

Open **Warehouse management > Setup > Warehouse management parameters**, go to the **Work** tab, and find the **Label preview to attachment** group.

| Field | Default | Purpose |
|---|---|---|
| **Print label preview to work attachment** | Off | Master toggle for mobile-flow label capture. When on, ZPL labels printed from mobile flows that have a related Work record are rendered and attached to that Work. Applies only to ZPL labels that have a related Work record. |
| **Label preview attachment turns off** | — | Read-only timestamp showing when capture will automatically disable itself. |
| **Auto-off days** | 14 | Number of days after the toggle is enabled before capture automatically turns off. |
| **Label preview retention days** | 14 | Age, in days, after which captured label-preview attachments are purged by the purge task. |
| **Default print density (DPI)** | 203 | Render density used as a fallback when a captured label does not declare its own size. |
| **Default label width (in)** | 4 | Render width, in inches, used as a fallback when a label does not declare a width (`^PW`). |
| **Default label height (in)** | 6 | Render height, in inches, used as a fallback when a label does not declare a length (`^LL`). |

The **Default print density (DPI)**, **Default label width (in)**, and **Default label height (in)** values are used only when a label does not declare its own dimensions. A label that specifies `^PW` / `^LL` renders at its declared size.

### Document routing (required for capture)

Mobile-flow capture only produces a label when a routing rule maps the work transaction to a label layout. Configure this under **Warehouse management > Setup > Document routing**:

1. Create or review the **Document routing** rule and lines that map the relevant work transaction type to the label layout you want captured.
2. The **printer name may be left blank** — no physical printer is needed for capture.

If no routing rule and line map the transaction to a layout, no label is produced and nothing is captured.

### Batch tasks (recommended)

Two periodic tasks support the capture feature. Schedule them under **Warehouse management > Periodic tasks**:

- **Render captured label previews** — the batch fallback that renders any captured labels not already rendered in-line and attaches them to their Work records. Scheduling this is recommended so no capture is missed.
- **Purge label preview attachments** — the retention clean-up task that removes captured label-preview attachments older than the configured **Label preview retention days**.

### Item identity display (Feature C)

Configuration lives under **Warehouse management > Setup > Mobile device**: enable the feature in **Item identity parameters**, define an ordered source chain in **Item identity profiles**, assign profiles to menu items in **Item identity menu item settings**, and place the line with the standard **Warehouse app field priority** setup. The [Item identity setup guide](docs/ItemIdentitySetup.md) walks through every step, including the recommended starting profile and how to move configuration between environments with data management.

### Blind PO receiving (Feature D)

Blind PO receiving is set up per warehouse:

1. Create a purchase pool for blind receipt orders under **Procurement and sourcing > Setup > Purchase orders > Purchase pools**. Warehouses can share a pool or each use their own.
2. Open **Warehouse management > Setup > Warehouse > Warehouses** and select a warehouse that receives blind freight. On the **Warehouse** FastTab, in the **Blind PO receiving** group, turn on **Enabled** and set **Blind receipt pool** to that pool.
3. Under **Warehouse management > Setup > Mobile device > Mobile device menu items**, create a menu item with **Mode** **Indirect**, **Work creation process** **Mixed license plate receiving**, and **Source document line identification method** **Blind PO receiving**. Add it to the workers' **Mobile device menu**.
4. Add a **Document routing** rule for the new menu item so license plate labels print. Put-away uses the existing purchase order work templates and location directives.
5. Schedule **Warehouse management > Periodic tasks > Post blind receipts** nightly in batch, in each company that uses the feature.
6. To let cancelled put-away work unregister a blind receipt and delete its lines, turn on **Unregister receipt when canceling work** in **Warehouse management parameters**.

Blind receipt orders can't use change management: an order in the blind receipt pool can't be saved while change management is on for it. If your company uses change management, allow override per vendor and turn it off for the clients' vendor accounts. The [setup guide](docs/BlindReceivingSetup.md) covers every step.

### Security

Assign the **Maintain warehouse label preview** duty to the roles that need this functionality. The duty bundles two privileges:

- **Generate label preview** — grants access to the on-demand preview button (Feature A).
- **Administer label preview capture** — grants access to the capture toggle and the render/purge batch tasks (Feature B).

For Feature C, assign the **Maintain item identity configuration** duty to the roles that configure the feature; it covers the three setup forms, the Test resolution diagnostic, and data management access to the item identity entities. Warehouse workers need no additional access — the identity line renders under their existing mobile-flow permissions.

For Feature D, assign the **Post blind receipts** duty to the role that schedules batch jobs, for example the warehouse manager; it grants access to the **Post blind receipts** batch job and isn't on any role by default. The warehouse settings are on the standard **Warehouses** form, leads create blind receipt orders with their existing purchasing access, and warehouse workers need no additional access.

Assign the duties (or the individual privileges) to your warehouse worker, supervisor, or administrator roles as appropriate.

## Usage

### Feature A — Generate a label preview

1. Open a layout: **Label layout** or **Document routing layout** (under Warehouse management setup).
2. Select a layout that has an active version (if it has no active version, the dialog will ask you to activate one first).
3. On the action pane, select **Generate label preview**. The **Label preview** dialog opens and renders the layout.
4. Optionally:
   - In the **Data source** group, pick a different **Record** and select **Refresh preview** to render against live data; the badge shows whether **Live record** or **Sample data** was used.
   - Edit the ZPL in the **Source ZPL (editable)** panel, select **Preview edits** to render without saving, then **Save to layout** to write it back to the active version. The **Translated ZPL (rendered result)** panel shows what the source renders to.
   - In the **Label size** group, turn on **Override label size** and set **Width (in)**, **Height (in)**, and **DPI** to force a render size.
   - Use **Rotate 90°** to rotate the current page, and **Previous label** / **Next label** to page through a multi-label job.
   - Use **Copy ZPL** to copy the final ZPL, or **Download PNG** to save the rendered image.

### Feature B — Capture mobile-flow labels to Work attachments

1. Enable capture: in **Warehouse management parameters > Work > Label preview to attachment**, turn on **Print label preview to work attachment**, and review the defaults (auto-off days, retention days, fallback DPI/width/height).
2. Confirm **Document routing** maps the relevant work transaction type to a label layout (the printer name may be blank).
3. (Recommended) Confirm the **Render captured label previews** task is scheduled as the batch fallback, and **Purge label preview attachments** for retention.
4. Run a warehouse mobile flow that prints a label tied to a Work record (for example, a flow that prints a work or container label).
5. Open the related **Work** record and view its attachments (the paperclip / Document handling). The rendered label image appears there as the **Label preview** document type, typically within moments of the label being built; if it is not there yet, it will be attached on the next render batch pass.

### Feature C — Show an item identity line on the warehouse app

1. Turn the feature on in **Item identity parameters**, create a profile in **Item identity profiles**, and assign it to your picking and receiving menu items in **Item identity menu item settings** (all under **Warehouse management > Setup > Mobile device**).
2. Open **Warehouse app field priority** once and place the **Item identity** field where you want it — typically directly under the Item field.
3. Run a configured flow on the mobile app; the identity line appears with the item information. Use the **Test resolution** action on the profile to replay any resolution from the rich client, source by source.

The [setup guide](docs/ItemIdentitySetup.md) covers every option; the [user guide](docs/ItemIdentityUserGuide.md) covers what workers see and troubleshooting. If the line does not appear, run the [smoke test](docs/ItemIdentitySmokeTest.md) — it isolates deployment, configuration, injection and resolution problems in about twenty minutes.

### Feature D — Receive freight without a purchase order

1. When freight arrives, the lead creates a purchase order with the client as **vendor account**, the receiving site and warehouse, and the warehouse's blind receipt pool as **purchase pool**. The order has no lines and change management off. The lead saves it and passes the order number to the worker with the paperwork.
2. On the mobile app, the worker opens the blind PO receiving menu item, scans the pallet's license plate, then scans or keys the order number.
3. For each item, the worker scans it, enters any product dimensions, the quantity and unit, and selects **OK**. Each confirmation adds a zero-price line to the order. Use **Show list** and **Modify** to change a quantity before the plate is complete.
4. The worker selects **LP Complete**. The plate is registered, one put-away work is created for it, and its label prints. The pallet is put away with the standard put-away flow.
5. Overnight, **Post blind receipts** posts a product receipt for the registered quantity, numbered after the order (`<PO number>-1`, then `-2` for the next night's posting).

The [setup guide](docs/BlindReceivingSetup.md) covers every option and how to check the setup; the [user guide](docs/BlindReceivingUserGuide.md) covers corrections and every message the device can show.

## Building from source

The repository includes an Azure DevOps pipeline (`azure-pipelines.yml`) that builds the package and both of its models from GitHub on a Microsoft-hosted agent and publishes deployable packages for both Lifecycle Services and Power Platform admin center environments — no build VM required. The one-time Azure DevOps setup (build tasks extension, package feed, GitHub connection) is described in the [build pipeline guide](docs/BuildPipeline.md).

## How it works and data privacy

- **100% in-process rendering.** ZPL is resolved, token-substituted, and rendered to PNG entirely within the AOS process. No label content, ZPL, or image is sent to any external service, and the features make no outbound network calls for rendering.
- **Single bundled assembly.** The renderer is delivered as one self-contained assembly, `AtomicAx.Zpl.Render.dll`, in the package `bin` folder. All of its managed dependencies are merged into that one file and the required native libraries are embedded in it, so there is no separate native-library deployment and no version conflict with other components in the AOS.
- **Capture stays in your environment.** Captured labels are buffered in a staging table inside your database and attached to the related Work record through standard Document handling. Capture is bounded by the auto-off and retention settings so data does not accumulate indefinitely.

## Troubleshooting

**Mobile-flow capture produces nothing.** Check, in order:
- **Print label preview to work attachment** is turned **on** in Warehouse management parameters, and has not auto-disabled (see the **Label preview attachment turns off** timestamp; re-enable it if the auto-off period has elapsed).
- **Document routing** has a rule and line that map the work transaction type to a label layout. Without this mapping, no label is produced, so there is nothing to capture.
- The label being printed is a **ZPL** label that has a **related Work record**. Labels with no Work record are not captured.

**A captured attachment has not appeared yet.** Rendering happens in near-real-time after the label is built, with a scheduled batch as a fallback. If the image is not on the Work record immediately, confirm the **Render captured label previews** task is scheduled and allow it to run.

**A label renders smaller or larger than expected, or a layout that declares no size renders at 4×6.** When a label does not declare its own dimensions (`^PW` / `^LL`), the render falls back to the parameter defaults — **Default print density (DPI)**, **Default label width (in)**, and **Default label height (in)** for capture, or 4×6 inches in the preview dialog if no size is otherwise specified. Set the parameter defaults to match your stock, or use **Override label size** in the preview dialog.

**A layout cannot be previewed.** The preview supports the **ZPL** definition type (including template blocks). Layouts whose definition type is **Variables** or **VariablesScript** are not previewable; the dialog reports this. If a layout has no active version, activate one first. If a template layout has no data source, add one to the layout so its template can be expanded.

## Third-party components

This solution redistributes third-party components inside the package `bin` folder, all under terms that permit commercial redistribution. See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for the full list of components and their terms.
