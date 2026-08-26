# AAX Warehouse Tools — ZPL Label Preview User Guide

This guide is written for functional consultants, WMS administrators and warehouse
users who configure and use the **ZPL Label Preview** solution delivered in the
`AAXWarehouseTools` model. It covers the two end-user features, the security objects
an administrator must assign, the messages a user may encounter, and the feature ensure
check-in an administrator will see in batch history (section 5).

The solution has two independent capabilities:

- **Feature A — On-demand label preview.** A *Generate label preview* button on the
  label-layout setup forms that renders a layout's ZPL to an on-screen image so a user
  can check a label *before* it is ever printed.
- **Feature B — Mobile-flow capture.** An opt-in parameter that automatically renders
  ZPL labels printed from mobile (RF) work flows to PNG images and attaches them to the
  related Work record, for audit/troubleshooting.

> *Screenshots are indicated as italic placeholder notes; insert the corresponding
> images in your own copy.*

---

## 1. On-demand label preview (Feature A)

### 1.1 Where the button lives

The solution adds an action-pane tab and a single button — **Generate label preview**
— to the two standard layout setup forms. The button group caption and the button text
are both *Generate label preview*.

| Form | Navigation path |
|------|-----------------|
| **Label layout** (`WHSLabelLayout`) | *Warehouse management > Setup > Document routing > Label layouts* |
| **Document routing layout** (`WHSDocumentRoutingLayout`) | *Warehouse management > Setup > Document routing > Document routing layouts* |

On each form, select the layout record you want to inspect, then choose
**Generate label preview**.

> *Screenshot: the action-pane tab "Generate label preview" on the Label layouts form.*

### 1.2 What happens on click

The button calls the preview controller, which:

1. Resolves the layout's ZPL (see the layout-type matrix in section 1.5).
2. Resolves a **seed record** — the most-recent record (highest RecId) on the table
   that naturally drives that layout (a license-plate label, container, or the layout's
   own data-source root table).
3. Substitutes the `$token$` placeholders in the ZPL using the standard warehouse
   document-routing translator. If a live seed record is found it is bound; any tokens
   it cannot fill (and any layout that found no live record at all) are filled with
   realistic sample values.
4. Renders the resulting ZPL to one or more PNG images and opens the **Label preview**
   dialog.

If a gate stops the preview (non-ZPL layout, no active version, template with no data
source), the dialog does **not** open; instead an information message is shown — see
section 4.

### 1.3 The Label preview dialog — a tour

The dialog caption is **Label preview**. It is a large dialog with an action pane at the
top and a body that stacks the badge, pager, data-source group, the rendered image and
the ZPL text.

> *Screenshot: the Label preview dialog with the rendered image, pager and ZPL panel.*

**Action pane.** Two button groups:

- Navigation group (caption *Label %1 of %2*):
  - **Previous label** — moves to the previous label page.
  - **Next label** — moves to the next label page.
  - Both buttons are shown only when the job produced more than one label, and each is
    disabled at the respective end of the range.
- File group (caption *Download PNG*):
  - **Download PNG** — opens the currently displayed image in the browser so the user can
    save it. The download honors the current rotation of the displayed page.
  - **Rotate 90°** — rotates the current page 90 degrees clockwise. Clicks are unlimited;
    rotation is tracked per page, so navigating away and back preserves each page's own
    rotation.

**Body.**

- **Badge** — a read-only line just above the image indicating the data behind the
  render (see section 1.4).
- **Pager** — read-only text *Label %1 of %2* (for example, *Label 1 of 3*) showing the
  current page and total number of label images.
- **Data source** group (caption *Data source*) — see section 1.6.
- **Image** — the rendered label PNG for the current page.
- **ZPL text** panel (group caption *Copy ZPL*) — a read-only, multi-line text box
  containing the final, token-substituted ZPL. This is the copy surface: select the
  text to copy it to the clipboard.

### 1.4 Badges

The badge tells the user what data produced the preview:

| Badge text | Meaning |
|------------|---------|
| **Live record: %1** | The preview bound a real seed record; `%1` is the record's title (or `RecId <n>` if the table has no title field). |
| **Sample data** | No live seed record was available, so all tokens were filled with synthetic sample values. |
| **Template: %1 label(s)** | A template-translator layout was expanded; `%1` is the number of labels produced by the expansion. |

### 1.5 Layout-type behavior matrix

| Layout / definition type | Behavior |
|--------------------------|----------|
| **Plain ZPL** (`WHSLabelLayout` with definition type *ZPL*, or any `WHSDocumentRoutingLayout`) | The active-version ZPL is read, tokens are substituted from the seed record + sample data, and the result is rendered. A single ZPL source may contain several `^XA…^XZ` blocks; each block becomes its own page in the pager. |
| **Template-translator layout** (`WHSLabelLayout` with *Enable template translator* = Yes — **now supported**) | The layout's `{{Header}}/{{Row}}/{{Footer}}` template is expanded against the layout's data-source query. Each row the query returns drives a row body; the layout's *Rows per label* / *Maximum labels* settings chunk those rows into one or more emitted labels, and each emitted label becomes one or more preview pages. The badge reads *Template: %1 label(s)*. If a template layout has **no usable data source**, the dialog does not open and the user sees: *"Add a data source to the layout to preview its template."* |
| **Variables / VariablesScript layout** | **Not supported for preview.** The dialog does not open; the user sees: *"Preview supports layouts with definition type 'ZPL' only."* |
| **No active version** (a ZPL layout whose active version yields no ZPL) | The dialog does not open; the user sees: *"Activate a layout version first."* |

### 1.6 The Data source group — picking the record that drives the output

The **Data source** group lets the user steer which record the preview renders from.
It shows, left to right:

- the **seed table label** (read-only text — the display name of the table being
  sourced, e.g. the license-plate label table or container table);
- **Record** — a selector (label *Record*) whose lookup lists records from that seed
  table, most-recent first, showing a friendly title column where one exists. Picking a
  record stores its identity for the next refresh; clearing the field means
  "auto-resolve the most-recent record";
- **Refresh preview** — re-renders the preview using the record currently chosen in the
  selector.

For **plain ZPL** layouts the chosen record is bound directly, so its field values
appear in the rendered label. For **template** layouts the chosen record scopes the
template's row expansion to that record; leaving the selector empty expands the full
query result, exactly as printing would.

Pressing **Refresh preview** clears all per-page rotation and re-renders from page 1.

### 1.7 Dynamic sample data — the empty-system story

Sample data fills tokens whenever no live data is available:

- When **no live seed record** exists on the seed table, the whole preview is built from
  sample values and the badge reads *Sample data*.
- When a live record **is** bound but the ZPL references tokens the record cannot fill
  (e.g. qualified tokens for other tables), those individual tokens are filled with
  realistic, type-aware sample values so the label still renders fully populated.

Sample values are realistic in length and shape — for example identifiers such as
`PO123456`, `LP000123`, `A0001`, `WK000123`, a quantity such as `12`, and dates that
default to today. This means a brand-new or empty environment can still produce a
meaningful, fully-populated preview of any ZPL layout.

> *Note:* for a template layout that returns **zero rows**, the row-level tokens remain
> unresolved by design (there is no live row to bind, and substituting sample data there
> would corrupt the populated case).

---

## 2. Mobile-flow capture (Feature B)

Feature B captures ZPL labels that are printed during mobile (RF) work execution and
attaches a rendered image of each label to the originating Work record. It is **opt-in**
and **auto-expiring**.

### 2.1 The parameter group

The parameters live on **Warehouse management parameters**
(*Warehouse management > Setup > Warehouse management parameters*), on the **Work** tab,
under the field group captioned **Label preview to attachment**.

| Field (label) | Type | Help / behavior |
|---------------|------|-----------------|
| **Print label preview to work attachment** | Yes/No | Master toggle. Help text: *"When enabled, ZPL labels printed from mobile flows are rendered as images and attached to the related work record. Automatically turns off after the configured number of days. Applies only to ZPL labels that have a related work record."* When switched on, the auto-off timestamp is initialized to *now + Auto-off days*. |
| **Label preview attachment turns off** | Date/time (read-only) | Shows when capture will automatically disable itself. |
| **Auto-off days** | Integer | Number of days after enabling before capture turns itself off automatically. Default when unset: **14**. |
| **Default print density (dots/mm)** | Integer | Render-density fallback used when the ZPL does not declare its own dimensions. Default when unset: **8** dpmm (203 DPI). |
| **Default label width (mm)** | Real | Width fallback used when the ZPL does not declare dimensions. Default when unset: **101.6** mm (4 in). |
| **Default label height (mm)** | Real | Height fallback used when the ZPL does not declare dimensions. Default when unset: **50.8** mm (2 in). |
| **Label preview retention days** | Integer | Age (in days) after which captured attachments and their staging rows are purged. Default when unset: **30**. |

> *Screenshot: the "Label preview to attachment" group on the Work tab of Warehouse
> management parameters.*

**Auto-off semantics.** Capture is not just a flag — it expires:

- Turning the toggle **on** sets *Label preview attachment turns off* to now plus the
  *Auto-off days* window.
- After that timestamp passes, capture stops. The flip-off is **lazy** and happens on
  first access after expiry: the next time either the print hot path or the render batch
  checks the toggle, an expired window flips the toggle back to **No** (under a lock) so
  it no longer captures. In other words, leaving the toggle on indefinitely is safe — it
  self-disables after the configured number of days, whether or not anyone reopens the
  parameters form.

### 2.2 What gets captured — and what does not

Capture stages a label **only** when both conditions hold at print time:

1. the capture toggle is on (and not expired); and
2. the label being printed has a **related Work record** (a non-empty WorkId).

This deliberately limits capture to ZPL labels that have a Work record to attach to. The
following are **explicitly NOT captured**:

- **Production license-plate labels** (`ProdLicensePlateLabelBuild`) — built without a
  Work record, so there is no attachment target.
- **WorkId-less reprints** — any reprint where the label carries no WorkId.
- **Wave labels** — a different print path, out of scope.
- **External print-service commands** (`ExternalLabelPrintServiceCommand`) — these are
  not raw ZPL, so there is nothing to render.

Capture sits on the universal ZPL print chokepoint, which fires *before* any batch-print
diversion — so a label sent to a batched printer is still captured.

### 2.3 The staging → batch → attachment flow

Capture is asynchronous, so it never slows down the mobile operator:

1. **Stage.** At print time, each qualifying final ZPL label is written to the
   **Label preview capture staging** buffer with status **Pending**. A per-print hash set
   prevents the same label being staged twice within one print operation. (If the print
   transaction rolls back, its staging row rolls back with it.)
2. **Render (batch).** The render batch picks up Pending rows, renders each to PNG(s) and
   attaches them to the Work record (see section 2.5).
3. **Attach.** Each rendered PNG is attached to the Work record as a *Label preview*
   document (document type `LBLPREV`), which appears under the Work record's attachments
   (paperclip).

**Where the images land and how they are named.** Attachments are reachable from the
**Work** record's attachments/paperclip. File names follow the pattern:

```
{WorkId}_{LayoutId}_{yyyyMMdd_HHmmss}_{seq}.png
```

(the timestamp is UTC; the `{LayoutId}` segment is omitted if the layout id is empty, and
`{seq}` is the 1-based label-block number when one ZPL produced several images).

### 2.4 Dedup on reprints

Each attachment records the SHA-256 hash of the captured ZPL. Before attaching, the
render batch checks whether the Work record already carries a *Label preview* attachment
with that same hash. If it does, the staging row is completed without attaching a
duplicate. The practical effect: reprinting an identical label to the same Work record
does **not** create duplicate images.

### 2.5 The render batch and the purge batch

Both batch jobs are under the standard Warehouse management menu:

| Job | Menu location | Purpose |
|-----|---------------|---------|
| **Render captured label previews** | *Warehouse management > Periodic tasks* | Renders Pending staging rows and attaches the images. |
| **Purge label preview attachments** | *Warehouse management > Periodic tasks > Clean up* | Deletes aged captured attachments and their terminal staging rows. |

> *Screenshot: the Render captured label previews and Purge label preview attachments
> menu items under Warehouse management.*

**Render batch parameters** (on the job's dialog):

- **Chunk size** — number of pending captured labels processed per batch pass
  (default **50**).
- **Maximum retries** — maximum render attempts before a failing row is marked *Poison*
  (default **3**).

Each staging row is processed independently in its own short transaction, so one failing
label never blocks the rest of the batch. The batch runs across all legal entities, and
each company's labels are rendered and attached inside that company.

**Purge batch.** The purge job is parameter-driven (it has no dialog of its own for the
window). It deletes only *Label preview* (`LBLPREV`) attachments on Work records and
*Done* / *Poison* staging rows that are older than the **Label preview retention days**
parameter. It never touches Pending, Processing or Error rows.

### 2.6 Staging row statuses

A staging row moves through these lifecycle statuses (field label **Status**):

| Status | Meaning |
|--------|---------|
| **Pending** | Staged label waiting for the render batch. |
| **Processing** | Staged label currently being rendered. |
| **Done** | Staged label rendered and attached — or terminally skipped (e.g. the Work record no longer exists, or a duplicate was deduped). |
| **Error** | Render/attach failed; the row will be retried on a later pass. |
| **Poison** | Failed too many times (reached *Maximum retries*); excluded from future pickup. |

If the target Work record was deleted between capture and render, the row is completed as
**Done** with the note *"The work record no longer exists; the label image was not
attached."* — it is never marked Error or Poison for this reason.

---

## 3. Security

The solution ships one duty and two privileges. An administrator must assign the duty
(or the individual privileges) to the appropriate **roles** — none are assigned to any
role out of the box.

| Object | Type | Label | Grants |
|--------|------|-------|--------|
| `AAXWHSLabelPreviewMaintain` | Duty | **Maintain warehouse label preview** | Bundles both privileges below. |
| `AAXWHSLabelPreviewView` | Privilege | **Generate label preview** | Access to the on-demand preview action (the *Generate label preview* button / `AAXWHSLabelPreview` menu item). Grants Feature A. |
| `AAXWHSLabelCaptureAdminister` | Privilege | **Administer label preview capture** | Access to the capture toggle and the two batch jobs (`AAXWHSLabelCaptureRender` and `AAXWHSLabelCapturePurge`). Grants the administrative side of Feature B. |

Recommended assignment:

- Give **Maintain warehouse label preview** to warehouse supervisor/administrator roles
  that should both preview layouts and administer capture.
- For users who should only preview layouts (not configure capture), assign just
  **Generate label preview**.

---

## 4. Troubleshooting for users

The messages below appear as on-screen information or error messages. Each is quoted
verbatim from the application.

| Message | When it appears | What to do |
|---------|-----------------|------------|
| **"Preview supports layouts with definition type 'ZPL' only."** | You pressed *Generate label preview* on a label layout whose definition type is Variables or VariablesScript. | Preview is only available for ZPL-type layouts. There is nothing to fix on a Variables layout — it is out of scope for preview. |
| **"Activate a layout version first."** | A ZPL layout has no active version (no ZPL to read). | Activate a version of the layout on the Label layouts form, then try the preview again. |
| **"Add a data source to the layout to preview its template."** | A template-translator layout (*Enable template translator* = Yes) has no usable data-source query to expand. | Add/configure the layout's data source so the template's rows can be resolved, then preview again. |
| **"The label could not be rendered: %1"** | The ZPL renderer rejected the label; `%1` carries the underlying renderer message. | Check the ZPL for syntax the renderer cannot parse. The trailing detail (`%1`) usually identifies the offending command. Correct the layout's ZPL and re-preview. |
| **"The work record no longer exists; the label image was not attached."** | (Capture/render batch) The Work record was completed, cancelled or purged between capture and rendering. | Informational. The staging row is closed as *Done*; no action is needed. |

For captured labels that never appear on a Work record, an administrator should check, in
order: the capture toggle is on and not expired (*Label preview attachment turns off*);
the label actually had a WorkId (production LP labels and WorkId-less reprints are never
captured); the **Render captured label previews** batch is scheduled and running; and the
staging rows are not in **Error** or **Poison** status (which would carry an error
message explaining the render failure).
---

## 5. Feature ensure (administrators)

Independently of Features A and B, the solution performs a short **feature ensure**
check-in that registers the installation with AtomicAx. Administrators may notice it in
two places:

- **Batch job history** — a job described **AAXWarehouseTools feature ensure** is
  enqueued automatically once at every AOS startup. It needs no scheduling, has no
  parameters, and is not attached to any menu item.
- **Infolog / batch log** — the job writes one of the messages below. In each message
  `%1` is the product, `AAXWarehouseTools`.

| Message | Meaning | What to do |
|---------|---------|------------|
| **"AAXWarehouseTools: feature '%1' ensured - state %2, allowed %3."** | The check-in succeeded; `%2` and `%3` report the state returned by the service. | Nothing. |
| **"AAXWarehouseTools: feature ensure for '%1' could not reach the service (offline or not yet deployed)."** | The AOS could not reach `api.licensing.atomicax.com`. | Allow outbound HTTPS (port 443) from the AOS to that host. The label features are unaffected; the check-in runs again at the next AOS startup. |
| **"AAXWarehouseTools: feature ensure for '%1' failed - continuing."** / **"… failed (CLR error) - continuing."** | An unexpected error occurred during the check-in. | Informational; the label features are unaffected. Report it to AtomicAx if it persists across restarts. |

The check-in sends environment identity only — tenant id, host URL, Lifecycle Services
environment id, hosting model, environment type and product/platform versions. It never
sends label content, ZPL, rendered images or any warehouse data, and it cannot block or
fail AOS startup.
