# AAX Warehouse Tools

In-product, zero-egress **ZPL label preview and mobile-flow label capture** for Microsoft Dynamics 365 Finance & Operations Warehouse management. ZPL is resolved, token-substituted, and rendered to PNG entirely **in-process inside the AOS** — no label or ZPL data ever leaves the environment.

Distributed as an ISV model: **`AAXWarehouseTools`**, Layer 14, publisher [www.atomicax.com](https://www.atomicax.com), model Id `896000929`.

---

## What it is

Two features, built on a single standalone C# rendering library (`AtomicAx.Zpl.Render`) that wraps [BinaryKits.Zpl.Viewer](https://github.com/BinaryKits/BinaryKits.Zpl) (SkiaSharp-based) and runs entirely within the AOS process.

### Feature A — On-demand label preview

A **Generate label preview** button added to the **Label layout** (`WHSLabelLayout`) and **Document routing layout** (`WHSDocumentRoutingLayout`) forms. It resolves the layout's ZPL, substitutes `$tokens$` via the standard `WhsDocumentRoutingTranslator` (the same engine used at print time), renders one PNG per `^XA…^XZ` label block, and shows them in a dialog. Beyond the basic preview, the dialog supports **image rotation**, **seed-record selection** (preview against a real warehouse record or generated **dynamic sample data**, badged Live vs. Sample), and honors **template-translator layouts** (`{{Header}}/{{Row}}/{{Footer}}` expansion over the layout's data source, exactly as printed). Variables/VariablesScript definition types are not previewable (they rely on Microsoft-internal APIs) and are declined with a clear message.

### Feature B — Mobile-flow label capture

Chain-of-Command hooks on the `WhsDocumentRouting` print path capture the **final, token-substituted ZPL** plus the Work context into a staging table as labels print from mobile flows. A scheduled batch renders the staged ZPL to PNG(s) and attaches them to the **Work** record (via Document Management, DocuType `LBLPREV`, surfaced through the standard attachments paperclip). Capture is gated by an **auto-expiring WHS parameter** (default 14 days) that mirrors the standard *Work creation history log* pattern, so it never stays on indefinitely. Production LP labels and non-ZPL print commands are out of scope by design.

## Documentation

| Document | Audience |
|---|---|
| [`docs/UserGuide.md`](docs/UserGuide.md) | WMS functional users & admins — using the preview, configuring capture, batches, security |
| [`docs/DeveloperGuide.md`](docs/DeveloperGuide.md) | Developers — architecture, build/deploy, testing, troubleshooting runbook, extending |
| [`ZPL_Label_Preview_Comprehensive_Plan.md`](ZPL_Label_Preview_Comprehensive_Plan.md) | Full implementation spec with verified codebase facts |
| [`ZPL_Label_Preview_Design_Doc.md`](ZPL_Label_Preview_Design_Doc.md) | Original requirements & design decisions |
| [`DECISIONS.md`](DECISIONS.md) | The implementation journey — every decision-rule application |
| [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) | Redistributed component licenses |

---

## Architecture at a glance

```
 Feature A (preview)                         Feature B (capture)
 ───────────────────                         ───────────────────
 WHSLabelLayout / WHSDocumentRoutingLayout   WhsDocumentRouting.printDocument  (CoC)
 forms  ──[Generate label preview]──┐        printLabelToPrinter (static CoC)  ──┐
                                    │          (final token-substituted ZPL)     │
 WhsDocumentRoutingTranslator       │                                            v
 ($token$ substitution, real        │        AAXWHSLabelCaptureStaging  (staging table,
  record or dynamic sample data)    │         gated by auto-expiring WHS parameter)
                                    │                                            │
                                    v                            scheduled batch │
        ┌───────────────────────────────────────────────┐       (AAXWHSLabelCaptureRender)
        │   AtomicAx.Zpl.Render.dll  (in-process, AOS)   │<──────────────────────┘
        │   ILRepack-merged, all deps internalized;      │
        │   win-x64 SkiaSharp/HarfBuzz natives embedded  │
        │   and self-extracted at first use → ONE .dll   │
        └───────────────────────────────────────────────┘
                 │ PNG (one per ^XA…^XZ block)
       preview ──┤
       capture ──┴──> Document Management ──> DocuRef on Work record (DocuType LBLPREV)
```

The entire renderer ships as a **single self-contained `AtomicAx.Zpl.Render.dll`**: every managed dependency (BinaryKits.Zpl, SkiaSharp, HarfBuzzSharp, ZXing.Net) is ILRepack-merged and internalized, and the Windows-x64 native libraries (`libSkiaSharp`, `libHarfBuzzSharp`) are embedded as resources and self-extracted beside the assembly at first use. This eliminates assembly-binding and version/identity collisions with the AOS-resident ElectronicReporting stack, and makes deployment a one-file drop. See [`DECISIONS.md`](DECISIONS.md) (entry *U-1 FINAL*) for why.

---

## Repository layout

```
AAXWarehouseTools/
├─ Descriptor/AAXWarehouseTools.xml          Model descriptor (Id 896000929, Layer 14)
├─ AAXWarehouseTools/                         X++ model metadata
│  ├─ AxClass/                                Preview + capture services, CoC extensions
│  │   ├─ AAXZplRenderService.xml             X++ wrapper over AtomicAx.Zpl.Render
│  │   ├─ AAXWHSLabelPreviewController.xml    Feature A: preview controller/context/sample data
│  │   ├─ AAXWHSLabelCaptureRender*.xml       Feature B: capture render batch + contract
│  │   ├─ AAXWHSLabelAttachmentService.xml    PNG → Work attachment (Document Management)
│  │   ├─ AAXWHSLabelCapturePurge.xml         Staging cleanup batch
│  │   ├─ WhsDocumentRouting_..._Extension    CoC capture seams (print path)
│  │   └─ WHSParameters_..._Extension         Auto-expiring capture toggle
│  ├─ AxTable/AAXWHSLabelCaptureStaging.xml   Capture staging table
│  ├─ AxTableExtension/WHSParameters...       Capture parameter fields
│  ├─ AxForm/AAXWHSLabelPreviewDialog.xml     Preview dialog (image, ZPL, rotate, seed)
│  ├─ AxFormExtension/                        Buttons on the two layout forms + params group
│  ├─ AxEnum, AxEdt, AxMenuItemAction/...     Supporting metadata
│  ├─ AxSecurityDuty / AxSecurityPrivilege/   Security (see Quick start)
│  ├─ AxLabelFile/                            AAXWarehouseTools_en-US label file
│  └─ AxReference/AtomicAx.Zpl.Render.xml     CLR reference to the merged DLL
├─ Projects/AAXWarehouseTools/
│  ├─ AAXWarehouseTools.sln                   Solution (model + C# lib + tests)
│  ├─ AAXWarehouseTools/AAXWarehouseTools.rnrproj
│  └─ AtomicAx.Zpl.Render/                    C# renderer (net472 + net8.0)
│      ├─ ZplRenderService.cs / ZplRenderResult.cs / ZplRenderException.cs
│      ├─ NativeLibraryPreloader.cs           Self-extracts embedded natives
│      └─ AtomicAx.Zpl.Render.csproj          ILRepack + embedded-natives build
├─ tests/AtomicAx.Zpl.Render.Tests/           xunit tests (net8.0 / net472)
├─ ZPL_Label_Preview_Comprehensive_Plan.md    Full implementation spec
├─ ZPL_Label_Preview_Design_Doc.md            Requirements & design decisions
├─ DECISIONS.md                               Recorded decision-rule applications
├─ THIRD-PARTY-NOTICES.md                     Redistributed component licenses
└─ README.md
```

---

## Requirements

- **D365 F&O Platform 7.0.7996+** — verified against build 7.0.7996.11 (AOSKernel.dll 7.0.7996.11) on a Windows Server 2022 dev box.
- **Visual Studio with the D365 F&O development tools** (or the `xppc` CLI) to compile the X++ model.
- **.NET SDK** to build the `AtomicAx.Zpl.Render` C# library. The library multi-targets **net472** (the AOS-loaded build) and **net8.0** (test-referenced only).
- Module references: ApplicationCommon, ApplicationFoundation, ApplicationPlatform, ApplicationSuite, Directory, SourceDocumentation, SourceDocumentationTypes.

---

## Quick start

1. **Clone** into your D365 packages directory (e.g. `C:\AOSService\PackagesLocalDirectory\`).

2. **Build the C# renderer.** From `Projects\AAXWarehouseTools\AtomicAx.Zpl.Render\`:
   ```
   dotnet build
   ```
   The net472 build runs ILRepack and copies the merged `AtomicAx.Zpl.Render.dll` into the model `bin\`. For a plain compile that skips ILRepack and deployment (e.g. on a non-AOS machine), pass:
   ```
   dotnet build -p:AAXSkipDeploy=true
   ```

3. **Compile the X++ model** via the Visual Studio add-in (Build models) or the `xppc` CLI (model module `AAXWarehouseTools`).

4. **Synchronize the database** (Dynamics 365 > Synchronize database) to create the `AAXWHSLabelCaptureStaging` table and `WHSParameters` extension fields.

5. **Assign security.** Grant the **`AAXWHSLabelPreviewMaintain`** duty (which bundles the `AAXWHSLabelPreviewView` and `AAXWHSLabelCaptureAdminister` privileges) to the warehouse roles that need preview and capture administration.

6. **Enable & schedule capture (Feature B).** Turn on the auto-expiring label-capture parameter under WHS parameters, then schedule the **label capture render** batch (and, optionally, the staging **purge** batch) from the Warehouse management periodic/clean-up menus.

---

## Status & verification

- **In-AOS render probe: PASSED.** Plain and barcode (`^BC`/ZXing) ZPL rendered in-process, PNG attached to a Work-area record via DocuType `LBLPREV`; the assembly-identity log confirmed only `AtomicAx.Zpl.Render` loads as a renderer assembly (no ElectronicReporting collision). Feature A preview confirmed working live. The temporary probe class was deleted after the gate passed.
- **Unit tests: all green** — 33 on net8.0 and 35 on net472 (the extra two exercise the native-preloader extraction path on .NET Framework; see `tests\AtomicAx.Zpl.Render.Tests`).
- **Tier-2 sandbox verification: pending** (deployable-package re-run of the probe).

See [`DECISIONS.md`](DECISIONS.md) for the verification record and the full decision history.

---

## Licensing

This solution is released under the **MIT License**.

It redistributes third-party components (BinaryKits.Zpl — MIT, SkiaSharp — MIT, HarfBuzzSharp — MIT, ZXing.Net — Apache-2.0) inside the model `bin\`. All permit commercial redistribution; full attributions are in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

---

Published by [atomicax.com](https://www.atomicax.com).
