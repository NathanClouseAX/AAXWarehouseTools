# ZPL Label Preview — Implementation Plan (`AAXWarehouseTools` model)

**Status:** Final — research-verified against this dev box
**Companion to:** `ZPL_Label_Preview_Design_Doc.md`
**Last updated:** 2026-06-03

## Context

ZPL label content in D365 F&O is authored as raw ZPL with `$tokens$` on the **Label layout** and **Document routing layout** forms, with no way to see the result without a physical Zebra printer or pasting into labelary.com (data egress). Per the design doc, this project adds — entirely in-product/in-tenant:

- **Feature A** — a "Generate label preview" button on both layout forms that renders the current layout to a PNG (real-record-else-sample token data).
- **Feature B** — capture of mobile-flow-generated labels: final ZPL staged at print time, asynchronously rendered and attached as PNGs to the related **Work** record, gated by an auto-expiring WHS parameter (modeled on the *Work creation history log*).

Rasterization: **BinaryKits.Zpl.Viewer** (.NET/SkiaSharp) wrapped in a C# class library, called from X++.

### Decisions confirmed (requirements session 2026-06-03)
- **Distribution: packaged ISV solution** → strong-name the wrapper DLL; deployable package carries all DLLs (incl. native `libSkiaSharp.dll` x64) in the model bin; license review (BinaryKits/SkiaSharp = MIT, ZXing.Net = Apache-2.0 — all redistribution-safe; record in repo).
- **Multi-label ZPL → separate PNG per `^XA…^XZ` block**, sequenced via `_{seq}` filename suffix.
- **Defaults confirmed:** auto-off 14 days, 8 dpmm (203 DPI), 4×2 in (101.6×50.8 mm) — all overridable in WHS parameters.
- **Work UI: standard attachments paperclip only** (no header link in v1).

### Key facts verified against this dev box (Platform 7.0.7996)

| Fact | Evidence |
|---|---|
| **Capture seam**: `WhsDocumentRouting.printDocument(WHSWorkTransType, WHSLicensePlateLabel _label)` (public, `ApplicationSuite\Foundation\AxClass\WhsDocumentRouting.xml:199`) has the label record but not the ZPL. ALL ZPL traffic funnels through `public static printLabelToPrinter(Name, str _label)` (line 349, obsolete-but-hookable) **before** the batch-print diversion (`WhsBatchedDocumentRoutingContext`, lines 351-357) | Read directly |
| `WHSLicensePlateLabel.WorkId` (EDT `WHSWorkId`, relation to `WHSWorkTable`) → label record resolves Work directly | Read directly |
| Token substitution is reusable: `WhsDocumentRoutingTranslator` (public, non-final) builder — `withRecord()`, `withRecordsFromQueryRun()`, `withParameterMap()`, `withLanguage()`, `translate(str)→str` | Explored |
| **Q-2 answered**: neither `WHSDocumentRoutingLayout` nor `WHSLabelLayout` has width/height/density fields → parse `^PW`/`^LL` from ZPL, fall back to parameters | Verified (grep) |
| Auto-off pattern to mirror: `WHSParameters.initWorkCreateHistoryLogUntilDate()` / `workCreateHistoryLogDurationInDays()` / `isWorkCreateHistoryLogEnabled()` lazy guard with pessimistic-lock flip-off (`WHSParameters.xml:280-707`) | Verified |
| Attachment API: `DocumentManagement::attachFileForRecord(Common, DocuTypeId, System.IO.Stream, fileName, attachmentName, notes)` (`ApplicationFoundation\AxClass\DocumentManagement.xml`); DocuType needs `ActionClassId = classNum(DocuActionFile)` | Explored |
| **R-1 partially de-risked**: `SkiaSharp.dll` (747KB) + `libSkiaSharp.dll` (11.4MB native) + ZXing already run inside this AOS (`ElectronicReporting\bin`) — but version must be reconciled with BinaryKits' pinned SkiaSharp dependency | Verified on disk |
| Model conventions from siblings (`AAXPOCloseHelper`, `AAXDataEntities`): Descriptor (Layer 14, Publisher `www.atomicax.com`), `Projects\` with `.sln` + `.rnrproj`, AxReference XML per external assembly, DLLs in model `bin\` | Explored |
| `AAXWarehouseTools\Descriptor` is **empty** — model must be bootstrapped from scratch | Verified |
| Purge/batch model: `WHSWorkCreateHistoryPurge` (RunBaseBatch + BatchRetryable, `#OCCRetryCount` retry, `WHSRecordDeletionCommitter`) | Explored |

### Naming convention
- C# assembly/namespace: **`AtomicAx.Zpl.Render`** (strong-named for ISV).
- X++ render wrapper: **`AAXZplRenderService`**.
- WHS-adjacent artifacts: **`AAXWHSLabelPreview*`** (Feature A) and **`AAXWHSLabelCapture*`** (Feature B) — AAX prefix mandatory for ISV model conflict-avoidance.

---

## Phase 0 — Model bootstrap + de-risk spike (gates everything)

### 0.1 Model bootstrap
- `Descriptor\AAXWarehouseTools.xml` — mirror `AAXPOCloseHelper\Descriptor\AAXPOCloseHelper.xml`: Layer 14, Publisher `www.atomicax.com`, fresh Id, `ModuleReferences`: `ApplicationPlatform`, `ApplicationFoundation`, `ApplicationCommon`, `ApplicationSuite`, `Directory`, `SourceDocumentation`, `SourceDocumentationTypes`.
- Source folders: `AAXWarehouseTools\AAXWarehouseTools\Ax<Type>\` (AxClass, AxTable, AxTableExtension, AxForm, AxFormExtension, AxEnum, AxEdt, AxMenuItemAction/Display, AxMenuExtension, AxSecurityPrivilege, AxSecurityDuty, AxLabelFile, AxReference).
- Project scaffolding: `Projects\AAXWarehouseTools\AAXWarehouseTools.sln` + `AAXWarehouseTools.rnrproj` (mirror `AAXDataEntities` pattern).

### 0.2 C# library `AtomicAx.Zpl.Render`
- `Projects\AAXWarehouseTools\AtomicAx.Zpl.Render\AtomicAx.Zpl.Render.csproj` — net8.0, strong-named (`.snk` in repo), NuGet: `BinaryKits.Zpl.Viewer` (+ transitive SkiaSharp, ZXing.Net). **Pick the BinaryKits version whose SkiaSharp dependency matches the version already in `ElectronicReporting\bin`** (check FileVersion first) to avoid AOS assembly-load conflicts.
- API (multi-label = list of per-label PNGs; dims parsed from `^PW`/`^LL` in C#, sentinel exception when absent so X++ owns fallback policy):

```csharp
namespace AtomicAx.Zpl.Render;
public static class ZplRenderService
{
    // one PNG per ^XA…^XZ block; dpmm/width/height of 0 = parse ^PW/^LL,
    // throw ZplDimensionsMissingException if absent
    public static IList<byte[]> RenderToPngList(string zpl, int dpmm = 0, double widthMm = 0, double heightMm = 0);
}
```

- Post-build: copy `AtomicAx.Zpl.Render.dll` + `BinaryKits.Zpl.*.dll` + `SkiaSharp.dll` + `libSkiaSharp.dll` (x64) + ZXing to `C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\bin\`.
- AxReference: `AAXWarehouseTools\AAXWarehouseTools\AxReference\AtomicAx.Zpl.Render.xml` (only the X++-facing assembly; transitive deps need no AxReference — same as ER's private Skia). Include real `PublicKeyToken` from signing.
- xunit spike harness: valid 4×2 ZPL, multi-label job, invalid ZPL (graceful `ZplRenderException`).

### 0.3 In-AOS render probe
Temporary runnable X++ class calling `AAXZplRenderService::renderToPngList()` and attaching the PNG to a scratch record — proves R-1 in the real AOS load context. **Delete before merge.** Re-run after deploying the package to a **Tier-2 sandbox** (the only true R-1 proof for ISV distribution).

**Exit criteria:** standalone + in-AOS render green; SkiaSharp version reconciled; X++ model compiles.

---

## Phase 1 — Feature A (on-demand preview)

### 1.1 `AAXZplRenderService` (X++ wrapper) — `AxClass\AAXZplRenderService.xml`

```
public static List renderToPngList(str _zpl, int _dpmm = 0, real _widthMm = 0, real _heightMm = 0) // List of container
```

- Catches `ZplDimensionsMissingException` → re-invokes with WHS parameter fallbacks (8 dpmm, 101.6×50.8).
- `System.Byte[]` → `container` via `System.IO.MemoryStream`/`Binary`; CLR exceptions unwrapped to readable analyzer messages.

### 1.2 Form extensions (button)
- `AxFormExtension\WHSLabelLayout.AAXWarehouseTools.xml` and `AxFormExtension\WHSDocumentRoutingLayout.AAXWarehouseTools.xml` — ActionPane button **`AAXGenerateLabelPreview`** + clicked handler invoking the controller with the current record.
- `WHSLabelLayout` DefinitionType handling: resolve ZPL through the layout's own `getLayoutSource()` path (same code printing uses) so `Variables`/`VariablesScript` types work when they produce ZPL; otherwise info message "Preview supports ZPL output only."

### 1.3 Context resolution — `AAXWHSLabelPreviewController` / `AAXWHSLabelPreviewContext` / `AAXWHSLabelPreviewSampleData`
- Optional seed dialog: one selector per bound data source (derived from `WHSLabelLayoutDataSource.DataSourceQuery` for new layouts; `WHSLicensePlateLabel` for legacy), pre-populated with most-recent record (QueryRun desc by RecId) — common path is one click.
- Auto-resolve most-recent record when skipped; **sample fallback** via `translator.withParameterMap(sampleMap, '')` with realistic-length values (`PO123456`, `LP000123`, dates = today).
- ZPL build **reuses `WhsDocumentRoutingTranslator`** — never reimplements token substitution.
- Tracks live-vs-sample per source for the dialog badge ("Live record: %1" / "Sample data").

### 1.4 Preview dialog — `AxForm\AAXWHSLabelPreviewDialog.xml`
- PNG bytes → temp blob store (`File::SendFileToTempStore`) → image control bound to URL (robust, browser-renderable, gives "Download PNG" for free). Avoid base64-in-HTML.
- Collapsible read-only final-ZPL text; **Copy ZPL** + **Download PNG** buttons; multi-label → tab/next-prev through per-label images.
- Render failure → analyzer error + offending ZPL inline.

### 1.5 Security
- `AxSecurityPrivilege\AAXWHSLabelPreviewView.xml` (read) on the preview menu item; role extension adds it to label-layout maintenance roles.

---

## Phase 2 — Feature B (capture → Work attachments)
*(2.1–2.2 can run parallel with Phase 1 after Phase 0.)*

### 2.1 WHSParameters extension + auto-off — mirror history-log pattern exactly
- `AxTableExtension\WHSParameters.AAXWarehouseTools.xml`: `AAXPreviewToWorkAttachment` (NoYes), `AAXPreviewAttachmentTurnsOff` (utcdatetime, display-only), `AAXPreviewAutoOffDays` (int, default 14), `AAXPreviewDefaultDpmm` (int, 8), `AAXPreviewDefaultWidthMm`/`HeightMm` (real, 101.6/50.8).
- Extension class `WHSParameters_AAXWarehouseTools_Extension`:
  - `aaxInitPreviewAttachmentTurnsOff()` → now + AutoOffDays (mirror `initWorkCreateHistoryLogUntilDate`, WHSParameters.xml:283).
  - `aaxIsPreviewToWorkAttachmentEnabled()` → lazy guard identical to `isWorkCreateHistoryLogEnabled` (WHSParameters.xml:669): true while now < TurnsOff; expired → pessimistic-lock, flip off, clear timestamp. Called on **both** the capture path and the batch.
  - `modifiedField` CoC: toggle On → init TurnsOff.
- `AxFormExtension\WHSParameters.AAXWarehouseTools.xml`: field group **"Label preview to attachment"** adjacent to the Work-creation-history-log group.

### 2.2 Staging table — `AxTable\AAXWHSLabelCaptureStaging.xml`
Fields: `FinalZpl` (memo), `ZplHash` (str64 SHA-256), `WorkId` (rel. WHSWorkTable), `LicensePlateId`, `LayoutId`, `PrinterName`, `Dpmm`, `WidthMm`, `HeightMm`, `Status` (new enum `AAXWHSLabelCaptureStatus`: Pending/Processing/Done/Error/Poison), `RetryCount`, `ErrorMessage`, `ProcessedDateTime`. Indexes: `(Status, RecId)` pickup; `(WorkId, ZplHash)` dedup.

### 2.3 Capture seams — **two CoCs with a context bridge** (verified design)
1. **`AxClass\WhsDocumentRouting_AAXWarehouseTools_Extension.xml`** — CoC on `printDocument(WHSWorkTransType, WHSLicensePlateLabel)`:
   - Gate: `if (!WHSParameters::find().aaxIsPreviewToWorkAttachmentEnabled()) { next(...); return; }` — zero overhead when off.
   - Open a disposable **`AAXWHSLabelCaptureScope`** (singleton/static-stack, `using`-style) stashing `_label.WorkId`, `LicensePlateId`, layout context; `next(...)`; dispose.
2. Same extension — CoC on static **`printLabelToPrinter(Name _printerName, str _label)`** (line 349 — the universal ZPL chokepoint, fires **before** the `WhsBatchedDocumentRoutingContext` diversion so batching cannot detach context):
   - If scope active + gate on → insert one staging row (final ZPL + scope context + hash); per-scope hash set prevents double-staging. Then `next(...)`.
- Covers both the legacy path (`printDocumentWithDocumentRoutingLine` → translate → print) and the new path (`WhsLicensePlateLabelPrintCommandGenerator` → `printLabelPrintCommandToPrinter` → ZPL case → `printLabelToPrinter`, WhsDocumentRouting.xml:391).

### 2.4 Render + attach batch — SysOperation
- `AAXWHSLabelCaptureRenderController` / `...Service` / `...Contract` (chunk size, max retries).
- Per chunk: mark `Processing` (with `#OCCRetryCount` deadlock retry per `WHSWorkCreateHistoryPurge` pattern) → **dedup**: skip if a `LBLPREV` DocuRef with same `ZplHash` already on that Work (hash stored in DocuRef.Notes) → render via `AAXZplRenderService` (**separate PNG per label block**) → attach each via `AAXWHSLabelAttachmentService` → `Done`. Failures: `Error` + RetryCount++; ≥ max → `Poison` (excluded from pickup).
- Batch start: re-check `aaxIsPreviewToWorkAttachmentEnabled()` (flips UI toggle off after expiry without a form visit) + `ensureDocuType()`.

### 2.5 Attachment helper — `AxClass\AAXWHSLabelAttachmentService.xml`
- `DocumentManagement::attachFileForRecord(workTable, 'LBLPREV', stream, fileName, name, notesWithHash)`.
- File name: `{WorkId}_{LayoutId}_{yyyyMMdd_HHmmss}_{seq}.png`.
- `ensureDocuType()` — idempotent create of `LBLPREV` ("Label preview") with `ActionClassId = classNum(DocuActionFile)` (RapidStartSetup pattern).
- Attachments surface via the standard Work-form paperclip (confirmed: no new Work UI in v1).

### 2.6 Security
- `AxSecurityPrivilege\AAXWHSLabelCaptureAdminister.xml` (maintain toggle + run batches); `AxSecurityDuty\AAXWHSLabelPreviewMaintain.xml` bundling both privileges.
- Menu items for render batch + purge under Warehouse management > Periodic tasks (menu extension).

---

## Phase 3 — Hardening
- **Purge batch** `AAXWHSLabelCapturePurge` (model: `WHSWorkCreateHistoryPurge` — chunked deletes, deadlock retry): deletes `LBLPREV` DocuRef/DocuValue older than `AAXPreviewRetentionDays` (param, default 30) + processed staging rows. Opt-in, recommended at go-live.
- Volume test: representative mobile LP-print flow — device latency unchanged (staging insert only), batch keeps up.
- Fidelity review (R-3): expose BinaryKits `DrawerOptions` font mapping in the wrapper; document "close, not pixel-perfect"; raw ZPL always shown beside the image.
- Optional ops list page over staging filtered to Error/Poison.

## Labels / EDTs / Enums
- `AxLabelFile\AAXWarehouseTools_en-US` — `@AAXWHT:` labels (button, dialog, badges, param group, errors, DocuType, menu items).
- New enum `AAXWHSLabelCaptureStatus`; new EDT `AAXWHSZplHash` (str64). Reuse existing `WHSZPL`, `WHSWorkId`, `WHSLicensePlateId`, `WHSLayoutId` EDTs.

## Verification
1. **C# standalone**: xunit fixtures (valid / multi-label / invalid ZPL) — `dotnet test`.
2. **X++ compile gate**: build the model (VS D365 add-in or `xppc`/msbuild on the `.rnrproj`) — clean compile.
3. **In-AOS probe** (Phase 0): render + attach inside AOS on this box.
4. **Manual smoke** (this box): preview button both forms — live record, empty-table sample path + badge, invalid-ZPL graceful error; toggle on → run a mobile LP flow → PNG(s) on the correct Work; set `AutoOffDays=1`, advance, confirm batch flips toggle to No; reprint → dedup (no duplicate attachment); purge removes only aged `LBLPREV`; security negative tests.
5. **Tier-2 sandbox** (ISV-critical): deploy package, re-run probe + one real mobile flow — final R-1 proof.

## Implementation order
P0.1 bootstrap → P0.2 C# lib + version reconcile → P0.3 in-AOS probe ⇒ then **parallel**: Feature A (1.1→1.4 → 1.5) ‖ Feature B params+staging (2.1, 2.2) → seams (2.3) → batch+attach (2.4, 2.5) → security (2.6) → Phase 3.

## Risks
| Risk | Mitigation |
|---|---|
| R-1 SkiaSharp native asset in AOS/Tier-2+ | Phase-0 probe before feature code; DLLs in model bin; Tier-2 re-proof; version reconciled with ER's existing Skia |
| SkiaSharp version collision with ElectronicReporting | Pick BinaryKits version matching ER's SkiaSharp FileVersion; verify load in probe |
| R-5 seam drift across versions | Seam verified on this build (WhsDocumentRouting.xml:199/349/391); add a smoke test that fails loudly if signatures change |
| Handheld latency | Cheap boolean gate + single staging insert; all rendering in batch |
| R-4 PII retention | Auto-off (lazy + batch guard) + retention purge |
| R-6 ISV licensing | BinaryKits/SkiaSharp MIT, ZXing.Net Apache-2.0 — redistribution OK; record license notices in repo |
