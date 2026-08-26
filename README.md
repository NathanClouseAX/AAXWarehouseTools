# AAX Warehouse Tools

On-demand ZPL label preview and printer-free mobile-flow label capture for Microsoft Dynamics 365 Finance & Operations (Warehouse management) — all rendering happens inside the AOS, with no label data leaving your environment.

## Overview

AAX Warehouse Tools adds two label capabilities to Warehouse management:

- **Generate a label preview on demand** — see exactly what a ZPL layout will produce, as a PNG image, without sending anything to a printer or to any external service.
- **Capture labels printed from mobile/RF flows and attach them to Work** — when enabled, ZPL labels that are printed from warehouse mobile flows and that belong to a Work record are rendered to images and attached to that Work, even when no physical printer is configured.

Both features render ZPL to images entirely in-process. The renderer is a single self-contained assembly (`AtomicAx.Zpl.Render.dll`) that ships in the model `bin` folder; there is nothing else to install, and the label features call no external service. Separately from the label features, the solution performs a short **feature ensure** check-in at AOS startup that reports the installation to AtomicAx. It sends environment identity only, never label data. See [Feature ensure](#feature-ensure).

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

## Prerequisites

- Microsoft Dynamics 365 Finance & Operations with the **Warehouse management** module enabled.
- Access to deploy a model into the environment and to run a database synchronization (a standard developer or admin task in a non-production environment, or a deployable package in production).
- System administrator (or equivalent) rights to configure Warehouse management parameters, Document routing, batch tasks, and security roles.
- Outbound HTTPS (port 443) from the AOS to the AtomicAx feature service (the host configured as `ApiBaseUrl` in the `AAXWarehouseToolsFeature` macro library) for the feature ensure check-in. If the host is unreachable, the check-in logs a warning and the solution continues; nothing in the label features depends on it.

No printer is required for either feature.

## Installation and database synchronization

1. **Deploy the `AAXWarehouseTools` model** from its deployable package (`AXDeployableRuntime_<platform>_<version>.zip`). Two first-party assemblies ship in the model `bin` folder and need no separate deployment: `AtomicAx.Zpl.Render.dll`, the self-contained ZPL renderer (no separate native-library deployment is required), and `AAXWarehouseTools.Feature.FnO.dll`, the feature ensure client.
2. **Synchronize the database.** This adds the Warehouse parameters fields used by the label-capture feature and the label-capture staging table that buffers labels between capture and rendering.

After the model is deployed and the database is synchronized, both features are available; the capture feature still needs to be enabled and configured (below).

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

A third task, **AAXWarehouseTools feature ensure**, needs no scheduling: it is enqueued automatically once at each AOS startup (see [Feature ensure](#feature-ensure)) and appears in batch job history under that description.

### Security

Assign the **Maintain warehouse label preview** duty to the roles that need this functionality. The duty bundles two privileges:

- **Generate label preview** — grants access to the on-demand preview button (Feature A).
- **Administer label preview capture** — grants access to the capture toggle and the render/purge batch tasks (Feature B).

Assign the duty (or the individual privileges) to your warehouse worker, supervisor, or administrator roles as appropriate.

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

## How it works and data privacy

- **100% in-process rendering.** ZPL is resolved, token-substituted, and rendered to PNG entirely within the AOS process. No label content, ZPL, or image is sent to any external service, and the features make no outbound network calls for rendering.
- **Single bundled assembly.** The renderer is delivered as one self-contained assembly, `AtomicAx.Zpl.Render.dll`, in the model `bin` folder. All of its managed dependencies are merged into that one file and the required native libraries are embedded in it, so there is no separate native-library deployment and no version conflict with other components in the AOS.
- **Capture stays in your environment.** Captured labels are buffered in a staging table inside your database and attached to the related Work record through standard Document handling. Capture is bounded by the auto-off and retention settings so data does not accumulate indefinitely.
- **The feature ensure check-in is the only outbound call.** It sends environment identity (tenant, host URL, environment id, hosting model, versions) to AtomicAx and nothing else: no label content, ZPL, images, or business data. Details in [Feature ensure](#feature-ensure).

## Feature ensure

The solution registers each installation with AtomicAx through a short **feature ensure** check-in. It is the solution's only outbound communication.

**When it runs.** Once at every AOS startup. The startup hook only enqueues the check-in as a batch task (description **AAXWarehouseTools feature ensure**), so startup is never delayed or blocked. No scheduling or configuration is required.

**Where it connects.** The AtomicAx feature service over HTTPS (port 443); the host is configured as `ApiBaseUrl` in the `AAXWarehouseToolsFeature` macro library. Allow outbound access to that host from the AOS.

**What it sends.** Environment identity only:

| Field | Value |
|---|---|
| Product | `AAXWarehouseTools` |
| Tenant | The Microsoft Entra tenant id of the environment |
| Host URL | The environment's base URL |
| Environment id | The Lifecycle Services environment id |
| Environment name | The host URL, or the environment id when no host URL is available |
| Environment type | `Prod` for a cloud environment registered in Lifecycle Services; blank for a development environment |
| Hosting | `Cloud` or `OnPremise` |
| Versions | The Finance & Operations application version and platform build version |

It never sends label content, ZPL, rendered images, warehouse data, or any other business data.

**What happens.** The service returns a signed token that the solution verifies locally, and the outcome is written to the Infolog and batch log as *AAXWarehouseTools: feature 'AAXWarehouseTools' ensured - state …, allowed …*. If the service cannot be reached, the task logs *AAXWarehouseTools: feature ensure for 'AAXWarehouseTools' could not reach the service (offline or not yet deployed).* and finishes. Every error in the check-in is caught and logged; it can never fault AOS startup or affect the label features.

## Troubleshooting

**Mobile-flow capture produces nothing.** Check, in order:
- **Print label preview to work attachment** is turned **on** in Warehouse management parameters, and has not auto-disabled (see the **Label preview attachment turns off** timestamp; re-enable it if the auto-off period has elapsed).
- **Document routing** has a rule and line that map the work transaction type to a label layout. Without this mapping, no label is produced, so there is nothing to capture.
- The label being printed is a **ZPL** label that has a **related Work record**. Labels with no Work record are not captured.

**A captured attachment has not appeared yet.** Rendering happens in near-real-time after the label is built, with a scheduled batch as a fallback. If the image is not on the Work record immediately, confirm the **Render captured label previews** task is scheduled and allow it to run.

**A label renders smaller or larger than expected, or a layout that declares no size renders at 4×6.** When a label does not declare its own dimensions (`^PW` / `^LL`), the render falls back to the parameter defaults — **Default print density (DPI)**, **Default label width (in)**, and **Default label height (in)** for capture, or 4×6 inches in the preview dialog if no size is otherwise specified. Set the parameter defaults to match your stock, or use **Override label size** in the preview dialog.

**A layout cannot be previewed.** The preview supports the **ZPL** definition type (including template blocks). Layouts whose definition type is **Variables** or **VariablesScript** are not previewable; the dialog reports this. If a layout has no active version, activate one first. If a template layout has no data source, add one to the layout so its template can be expanded.

**The batch history shows *feature ensure … could not reach the service*.** The AOS could not reach the AtomicAx feature service. Confirm outbound HTTPS (port 443) to the host configured as `ApiBaseUrl` in the `AAXWarehouseToolsFeature` macro library is allowed from the AOS. The message is a warning only: the label features keep working, and the check-in runs again at the next AOS startup.

## Third-party components

This solution redistributes third-party components inside the model `bin` folder, all under terms that permit commercial redistribution. See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for the full list of components and their terms.
