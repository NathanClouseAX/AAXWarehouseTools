# Design Document — ZPL Label Preview in Dynamics 365 Finance & Operations (Warehouse Management)

**Status:** Draft for review
**Author:** (fill in)
**Last updated:** 2026-06-03
**Audience:** D365 F&O technical team (X++ developers, solution architect), WMS functional lead

---

## 1. Purpose

Today, ZPL label content in D365 F&O is authored as raw ZPL text with field tokens (e.g. `$OrderNum$`, `$PurchLine_1.ProjId$`) on the **Label layout** and **Document routing layout** forms. There is no way to *see* what a label will look like without sending it to a physical Zebra printer (or pasting it into an external viewer such as labelary.com — which means warehouse data leaves the tenant).

This project adds two capabilities, both rendered **in product** and **entirely locally** (no label data is sent to any third-party service):

- **Feature A — On-demand preview.** A **"Generate label preview"** button on the Label layout and Document routing layout forms that renders the current layout to an image so an author can iterate on a design without printing.
- **Feature B — Mobile-flow capture.** Labels that are actually generated during warehouse mobile-device flows (LP labels produced via document routing / label layouts) are rendered to images and **attached to the related Work record**, so they can be reviewed after the fact. This is gated by an auto-expiring Warehouse management parameter modeled on the existing *Work creation history log*.

---

## 2. Scope

### In scope
- Rendering ZPL → image (PNG) locally inside F&O.
- On-demand preview button on **Label layout** (`Warehouse management > Setup > Document routing > Label layout`) and **Document routing layout** (`Warehouse management > Setup > Document routing > Document routing layouts`).
- Token substitution for preview using a **real record when available, else synthetic sample data**.
- Capture of mobile-flow-generated labels and attachment of the rendered image to the related **Work** (`WHSWorkTable`).
- A **Warehouse management parameter** toggle ("print label preview to Work attachment") that **auto-disables after a configurable period (default 14 days)**, mirroring the Work creation history log behavior.

### Out of scope (initial release)
- A standalone browsable "gallery" page of all generated labels (explicitly deferred — attachments chosen instead). Can be a Phase 2 add-on.
- Changing how labels are *printed* (the Document Routing Agent path is untouched).
- ER (Electronic Reporting) based label formats. This design targets the **`WHSDocumentRouting`** label path (license-plate / document-routing layouts), which is what the mobile flows in question use. An ER-format equivalent can be a later extension.
- EPL/DPL or non-ZPL label languages (renderer support permitting).

---

## 3. Locked design decisions (from requirements interview)

| # | Decision | Choice |
|---|----------|--------|
| 1 | ZPL rasterization approach | **In-product, local .NET renderer** — no third-party server, no data egress |
| 2 | Preview token data | **Real bound record if available, else synthetic sample data** |
| 3 | Mobile-flow delivery | **Attach rendered image files to the Work** (browsable page deferred) |
| 4 | Capture toggle | **WHS parameter, auto-off after a set period** (default 14 days), modeled on Work creation history log |

### 3.1 Important correction on the renderer
The requirement referenced **OpenLabel (Dwarf1er)** as the local renderer. OpenLabel is a good fit in *spirit* (C#/.NET, local, no third-party server) but per its own documentation it does **ZPL templating, DPI scaling, and network printing — it does not rasterize ZPL into a viewable image.** It cannot, on its own, produce the preview picture this feature needs.

**Recommendation:** use **`BinaryKits.Zpl.Viewer`** for the rasterization step. It is a C#/.NET library (SkiaSharp-based) whose explicit purpose is to render ZPL to an image *locally*, as a free, in-process alternative to labelary.com. OpenLabel can still be used **alongside** it if its DPI-scaling/templating helpers are wanted, but the actual "make me a picture" job belongs to the Viewer.

Renderer options considered (all keep data in-tenant):

| Library | Language | In-process in F&O? | Notes |
|---|---|---|---|
| **BinaryKits.Zpl.Viewer** *(recommended)* | C#/.NET | Yes (managed DLL + SkiaSharp native asset) | Free, `ZplAnalyzer` → `ZplElementDrawer.Draw()` → PNG bytes. Carries a native SkiaSharp dependency — see Risk R-1. |
| OpenLabel (Dwarf1er) | C#/.NET | Yes | Templating + DPI scaling + network print **only — no rasterization**. Optional helper, not the renderer. |
| zebrash | Go | No (would need a self-hosted sidecar) | Renders to PNG; self-hostable. Breaks the "in-process / in-product" preference; only if R-1 blocks SkiaSharp. |
| Neodynamic ZPLPrinter Emulator SDK | .NET | Yes | **Commercial/licensed**, but the cleanest pure-.NET deployment story; fallback if native-asset deployment of SkiaSharp proves troublesome. |

---

## 4. Background — how D365 builds and prints these labels

The mobile-flow LP labels in question flow through the standard **`WHSDocumentRouting`** framework:

- `WHSDocumentRouting.printDocument(...)` fetches the document routing, runs a **`translate`**-style step that replaces the layout's placeholders/tokens with actual Dynamics values, producing the **final ZPL string**, and then sends that string to the printer.
- The final hand-off to the printer is `WHSDocumentRouting::printLabelToPrinter(printerName, zplString)`, which routes the ZPL to the **Document Routing Agent (DRA)** and on to the physical printer.

The key insight for **Feature B**: the *fully token-substituted, final ZPL* already exists inside this framework just before it is sent to the printer. We do not need to re-implement token substitution for capture — we intercept the finished ZPL. (Method names/signatures must be confirmed against the target build; see Open Questions Q-1.)

For **Feature A**, the same `WHSDocumentRouting` placeholder-substitution logic is reused to turn the layout being edited into final ZPL, against either a real seed record or sample data, before rendering.

---

## 5. Solution architecture (high level)

```
                         ┌──────────────────────────────────────────┐
                         │  ZPL Render Service (X++ wrapper over      │
                         │  BinaryKits.Zpl.Viewer .NET assembly)      │
                         │  zpl + dpmm + W×H  ->  PNG bytes           │
                         └──────────────────────────────────────────┘
                               ▲                         ▲
                               │ final ZPL               │ final ZPL
        ┌──────────────────────┴───────┐     ┌───────────┴───────────────────────┐
        │ FEATURE A: On-demand preview │     │ FEATURE B: Mobile-flow capture     │
        │ Button on Label layout &     │     │ CoC on WHSDocumentRouting print    │
        │ Document routing layout forms│     │ path -> stage ZPL + Work context   │
        │ -> build ZPL (real|sample)   │     │ -> async batch renders + attaches  │
        │ -> render -> show in dialog  │     │    PNG to WHSWorkTable (DocuRef)   │
        └──────────────────────────────┘     └────────────────────────────────────┘
                                                          ▲
                                                          │ gated by
                                              ┌───────────┴─────────────────┐
                                              │ WHSParameters toggle         │
                                              │ "Preview to Work attachment" │
                                              │ auto-off after N days        │
                                              └──────────────────────────────┘
```

---

## 6. Rendering component (shared by A and B)

### 6.1 Library integration
- Add a **C# class library project** to the F&O solution (same package/model) that references the `BinaryKits.Zpl.Viewer` NuGet package and its dependencies (SkiaSharp + native assets, ZXing.Net, etc.).
- Expose a thin, F&O-friendly API, e.g.:

```csharp
public static class ZplRenderService
{
    // returns PNG bytes; throws on invalid ZPL
    public static byte[] RenderToPng(string zpl, double labelWidthMm, double labelHeightMm, int dpmm);
}
```

- Internally: `ZplAnalyzer.Analyze(zpl)` → for each `LabelInfo`, `ZplElementDrawer.Draw(elements)` → PNG bytes. Multi-label ZPL (`^XA…^XZ` repeated) yields multiple images — return a list / combine vertically (decision: return per-label list; caller decides).

### 6.2 Render parameters (DPI + dimensions)
Rasterizing needs **print density (dpmm)** and **label size (W×H)**. Resolution order:
1. Parse `^PW` (print width, dots) and `^LL` (label length, dots) from the ZPL when present; combine with density to compute mm.
2. Use the **label layout's configured size** if stored (the layout description shows sizes like "4 x 2", but confirm whether this is a structured field or free text — see Q-2).
3. Fall back to defaults: **8 dpmm (203 DPI)** and **4 in × 2 in (101.6 mm × 50.8 mm)** for LP labels, both **overridable** via Warehouse management parameters so this isn't hard-coded.

### 6.3 X++ ↔ .NET
Call the managed assembly directly from X++ (standard interop). Return `System.Byte[]`; convert to `container`/temp file via `Binary` / `System.IO.File` as needed for display and attachment.

---

## 7. Feature A — "Generate label preview" button

### 7.1 UX
- New button **`Generate label preview`** on the action pane of:
  - **Label layout** form (`WHSLabelLayout`).
  - **Document routing layout** form (`WHSDocumentRoutingLayout`).
- On click:
  1. Resolve render context (real-else-sample, §7.2).
  2. Build final ZPL by running the layout text through the standard placeholder-substitution logic.
  3. Render via `ZplRenderService`.
  4. Display the PNG in a **preview dialog** (image control). Include the resolved final ZPL in a collapsible text box for debugging, plus a **"Copy ZPL"** and **"Download PNG"** action.
  5. If rendering fails (invalid ZPL), show the parse error and the offending ZPL inline rather than a generic error.

### 7.2 Preview data — "real record if available, else sample"
The layout binds data sources (e.g. `InventTable_1` (Items), Purchase order lines, `WHSWorkTable_1` (Work), License plate). To fill tokens:

1. **Optional seed dialog.** When the button is pressed, offer a lightweight dialog letting the author optionally pick a seed record per bound data source (e.g. a License plate ID, a Work ID, an Item). Pre-populate each selector with the **most recent** matching record so the common path is one click.
2. **Auto-resolve.** If the author skips selection, automatically bind the most recent valid record for each required data source.
3. **Sample fallback.** For any token with no resolvable record (or empty tables in a fresh environment), substitute **synthetic sample values** from a maintainable sample-data map (e.g. `OrderNum = "PO123456"`, `LicensePlateId = "LP000123"`, dates = today). Sample values should be realistic in *length* so layout/overflow issues surface in preview.
4. Clearly badge the preview as **"Sample data"** vs **"Live record: <id>"** so the author knows which they're looking at.

### 7.3 Classes / artifacts (Feature A)
- `WHSLabelPreviewController` — orchestrates context resolution, ZPL build, render, dialog.
- `WHSLabelPreviewContext` — holds seed records + sample-data map.
- Form extensions: add button + event handler on `WHSLabelLayout` and `WHSDocumentRoutingLayout`.
- `WHSLabelPreviewDialog` (form) — image + ZPL + copy/download.
- Security: `WHSLabelPreviewView` privilege (see §10).

---

## 8. Feature B — Capture mobile-flow labels to Work attachments

### 8.1 Interception (the seam)
Use **Chain of Command (CoC)** to wrap the `WHSDocumentRouting` print path and capture the **final, token-substituted ZPL** plus the work/LP context.

- Primary candidate: wrap **`WHSDocumentRouting.printDocument(...)`** (it has both the final ZPL and the originating routing/context).
- `printLabelToPrinter(printerName, zpl)` is the narrowest chokepoint (every label passes through it) but carries the *least* context (no Work ID). If we must hook there, the Work/LP context has to be threaded down — prefer the higher method that still has the originating record.
- **Confirm against build** which method carries the buffer + context for the installed version (Q-1). Whichever we choose, the design contract is: *"intercept finished ZPL + enough context to resolve the target `WHSWorkTable` record."*

### 8.2 Gating
Inside the CoC wrapper, do nothing unless the **`WHSParameters` toggle** (§9) is currently **on** (and not expired). When off, zero overhead beyond a cheap boolean check.

### 8.3 Decouple rendering from the device flow (performance)
Mobile flows can emit very high label volumes; rendering synchronously on the worker's confirm would slow the handheld. Therefore:

1. **Synchronously** (cheap): write a row to a **staging table** `WHSLabelCaptureStaging` with: final ZPL, resolved `WHSWorkId` (and `LicensePlateId`, layout id, printer, dimensions, dpmm, created datetime, a ZPL hash). This adds negligible latency to the device step.
2. **Asynchronously** (a recurring batch, e.g. every few minutes): pick up unprocessed staging rows, render each via `ZplRenderService`, attach the PNG to the resolved Work via the **DocuRef** framework, mark the row processed (or delete).
   - **Dedup:** skip rendering if an identical ZPL hash is already attached to the same Work (optional, recommended — LP reprints are common).
   - **Resilience:** failures stay in staging with an error + retry count; poison rows after N retries.

> If a strictly real-time attachment is preferred over batch, the render+attach can run inline in the CoC wrapper — but this is **not recommended** for high-volume flows. Default to the staging + batch model.

### 8.4 Attachment details
- Attach to `WHSWorkTable` via `DocumentManagement` / `DocuRef` + `DocuValue`.
- New **document type** `LBLPREV` ("Label preview") so these are filterable and purgeable.
- File name convention: `{WorkId}_{LayoutId}_{yyyyMMdd_HHmmss}_{seq}.png`.
- Attachments surface automatically in the standard **Attachments** flyout / paperclip badge on the Work details form (the count indicator visible in the work header), so no new UI is strictly required — the "labels generated" affordance is the attachment count.
- *(Optional polish, low cost):* a "Labels generated (n)" info link in the Work header that opens the Attachments filtered to `LBLPREV`. Recommended if cheap; otherwise the standard paperclip suffices.

### 8.5 Classes / artifacts (Feature B)
- `WHSLabelCaptureStaging` (table) — staging buffer.
- `WHSDocumentRouting_Extension` (CoC) — capture hook.
- `WHSLabelCaptureRenderBatch` (RunBaseBatch / SysOperation) — async render + attach.
- `WHSLabelAttachmentService` — DocuRef attach helper.
- (optional) `WHSLabelCapturePurge` (batch) — retention cleanup (§12).

---

## 9. Data model changes — `WHSParameters`

Mirror the **Work creation history log** pattern exactly so it is familiar to admins.

| Field | Type | Behavior |
|---|---|---|
| `PreviewToWorkAttachment` | `NoYes` enum | The toggle ("Print label preview to Work attachment"). |
| `PreviewAttachmentTurnsOff` | `utcdatetime` | Display-only timestamp when capture auto-disables. |
| `PreviewAutoOffDays` | `int` (default **14**) | Configurable auto-off window. Set per the ask; default 2 weeks. |
| `PreviewDefaultDpmm` | `int` (default **8**) | Render density fallback (§6.2). |
| `PreviewDefaultWidthMm` / `PreviewDefaultHeightMm` | `real` (default 101.6 / 50.8) | Render size fallback. |

**Auto-off mechanics (matching the history-log feature):**
- When `PreviewToWorkAttachment` is flipped **On**, set `PreviewAttachmentTurnsOff = now + PreviewAutoOffDays`.
- A guard checks expiry: if `now > PreviewAttachmentTurnsOff`, treat the toggle as Off and flip it Off + clear the timestamp. Implement the guard **both** (a) lazily on the capture path (so an expired toggle never captures), **and** (b) in the render batch / a tiny scheduled job (so the UI flips to "No" without someone visiting the form).

UI placement: new field group **"Label preview to attachment"** under the **License plates / Reports** area of the Warehouse management parameters General tab, adjacent to the existing **Work creation history log** group for discoverability.

---

## 10. Security

- **Duty:** `Maintain warehouse label preview`.
- **Privileges:**
  - `WHSLabelPreviewView` — run the on-demand preview button (read-level; attaches to label-layout maintenance roles).
  - `WHSLabelCaptureAdminister` — edit the WHS parameter toggle and run/cleanup batches (admin-level).
- Menu items (Action/Output/Display) for the dialog, batch, and purge each tied to the appropriate privilege.
- Attachments inherit standard DocuRef security on `WHSWorkTable`.

---

## 11. Performance & volume considerations

- On-demand preview is single-shot and user-initiated — negligible.
- Capture: synchronous staging write only (one insert) on the device path → minimal latency impact.
- Rendering cost (SkiaSharp) is borne by a **batch**, sized/scheduled by the team; parallelism via batch tasks if needed.
- **Dedup by ZPL hash** materially reduces render volume where the same label is reprinted.
- Storage: PNGs are small but high label volumes add up — see retention.

---

## 12. Retention / cleanup (recommended)

The auto-off toggle stops *new* captures after the window, but **existing attachments persist**. To prevent unbounded growth:

- `WHSLabelCapturePurge` batch deletes `LBLPREV` attachments (and processed staging rows) older than a configurable `PreviewRetentionDays` (suggest default 30).
- Make purge opt-in but recommend enabling it at go-live.

---

## 13. Risks & mitigations

| ID | Risk | Mitigation |
|---|---|---|
| **R-1** | **SkiaSharp native assets** (`BinaryKits.Zpl.Viewer` dependency) may not deploy/run cleanly inside the F&O AOS process, especially on Microsoft-managed (Tier-2+/PROD) environments where you cannot hand-install components. | Validate end-to-end render in a **Tier-2 sandbox early** (spike before committing). Ensure the correct `SkiaSharp.NativeAssets.*` for the AOS OS ships in the deployable package's bin. **Fallback:** self-hosted renderer (zebrash) or commercial pure-.NET SDK (Neodynamic) — both keep data in-tenant. |
| **R-2** | OpenLabel mistaken for a rasterizer. | Addressed in §3.1 — Viewer does the image; OpenLabel optional. |
| **R-3** | Render fidelity: third-party engines emulate a *subset* of ZPL; custom fonts (`^A@`/downloaded fonts) and exotic commands may render imperfectly. | Set expectation that preview is "close, not pixel-perfect." Provide font-mapping config where the Viewer supports it. Always show the raw ZPL alongside the image. |
| **R-4** | Capturing personal/customer data into attachments (e.g. customer name on labels) creates a data-retention/PII footprint. | Auto-off toggle + retention purge limit exposure; document for the customer's data-governance review. |
| **R-5** | Hooking the wrong/changed framework method across versions. | Confirm method signature in target build (Q-1); cover with build-validation test. |
| **R-6** | License/compatibility of chosen library with the customer's distribution model (ISV vs internal). | Confirm license terms (BinaryKits is open source; Neodynamic is commercial) against how the solution is shipped. |

---

## 14. Open questions

- **Q-1:** On the target F&O build, which `WHSDocumentRouting` method exposes the *final token-substituted ZPL* together with enough context to resolve the originating `WHSWorkTable` record? (Decompile/confirm `printDocument` vs alternatives.)
- **Q-2:** Is label size on the layout a **structured field** we can read for render dimensions, or free text in the description? Determines §6.2 step 2.
- **Q-3:** Target F&O version / update level (affects available extension points and SkiaSharp/.NET runtime).
- **Q-4:** Distribution model — internal model in the customer's package, or a packaged ISV solution? (Affects R-6 and how the .NET DLL is referenced.)
- **Q-5:** Default auto-off window confirmed at **14 days**? Default render density confirmed at **203 DPI (8 dpmm)** and default size **4×2 in**?
- **Q-6:** Is the optional "Labels generated (n)" Work-header link wanted, or is the standard attachment paperclip sufficient for v1?
- **Q-7:** Should multi-label ZPL (one print job → several `^XA…^XZ`) attach as **separate PNGs** or one combined image?

---

## 15. Phasing / implementation checklist

**Phase 0 — De-risk (do first)**
- [ ] Spike: reference `BinaryKits.Zpl.Viewer` in a C# class lib, render a sample ZPL to PNG **inside a Tier-2 F&O sandbox**. (Resolves R-1.)
- [ ] Confirm the capture seam method + context (Q-1).

**Phase 1 — Feature A (on-demand preview)**
- [ ] `ZplRenderService` wrapper.
- [ ] Preview button + dialog on Label layout & Document routing layout forms.
- [ ] Real-else-sample context resolution + sample-data map.
- [ ] Security privilege.

**Phase 2 — Feature B (capture to attachment)**
- [ ] `WHSParameters` fields + auto-off mechanics (mirror history log).
- [ ] CoC capture hook → staging table.
- [ ] Async render + DocuRef attach batch; dedup.
- [ ] Document type `LBLPREV`; (optional) Work-header link.

**Phase 3 — Hardening**
- [ ] Retention/purge batch.
- [ ] Volume/perf test on a representative mobile flow.
- [ ] Font-mapping / fidelity review.

---

## 16. Test plan (key cases)

- Preview a valid layout with a live record; verify image matches expected layout.
- Preview with empty tables → sample data path; badge shows "Sample data."
- Preview invalid ZPL → graceful parse error + raw ZPL shown.
- Toggle on → capture during a mobile LP-print flow → PNG attached to correct Work.
- Toggle auto-off: set window to 1 day, advance, confirm capture stops and toggle flips to No without manual form visit.
- High-volume flow: confirm device step latency unchanged (staging only) and batch keeps up.
- Dedup: reprint same label → no duplicate attachment.
- Retention purge removes aged `LBLPREV` attachments only.
- Security: user without privilege cannot preview / cannot edit toggle.

---

## 17. References

- BinaryKits.Zpl / BinaryKits.Zpl.Viewer (local ZPL→image renderer, .NET/SkiaSharp): https://github.com/BinaryKits/BinaryKits.Zpl • https://www.nuget.org/packages/BinaryKits.Zpl.Viewer
- OpenLabel (Dwarf1er) — ZPL templating/scaling/network print (not a rasterizer): https://github.com/Dwarf1er/openlabel
- zebrash (Go, self-hostable ZPL→PNG, fallback): https://github.com/ingridhq/zebrash
- D365 `WHSDocumentRouting` label print path (`printDocument`, `translate`, `printLabelToPrinter`): community write-ups on ZPL label design & D365 F&O integration.
- Microsoft Learn — ER-based ZPL label design (alternative path, out of scope): https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/analytics/er-design-zpl-labels
