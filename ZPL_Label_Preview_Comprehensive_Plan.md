# ZPL Label Preview — Comprehensive Implementation Plan (AI-Executable Spec)

**Status:** Final — fully code-verified against this dev box (Platform 7.0.7996.11, AOSKernel.dll 7.0.7996.11)
**Supersedes:** `ZPL_Label_Preview_Implementation_Plan.md` (kept for history)
**Companions:** `ZPL_Label_Preview_Design_Doc.md` (requirements & decisions), `reference\links.txt`
**Last updated:** 2026-06-03

**Audience: an AI software-development agent.** This document is the complete work order. Part I gives ground rules, Part II gives verified codebase facts (cite by F-number), Part III gives methodologies & patterns (follow exactly), Part IV gives requirements (REQ-IDs — every one must be satisfied and traceable), Part V gives ordered work packages (WP-IDs with steps, file specs, acceptance criteria), Part VI gives the verification matrix, Part VII gives decision rules and risk handling.

Key words **MUST**, **MUST NOT**, **SHOULD**, **MAY** are RFC-2119.

---

# PART I — Mission brief & ground rules

## 1. Mission

Build two features in a new D365 F&O ISV model **`AAXWarehouseTools`**, plus a C# rendering library **`AtomicAx.Zpl.Render`** wrapping **BinaryKits.Zpl.Viewer** (SkiaSharp-based, in-process, zero data egress):

- **Feature A — On-demand preview:** a "Generate label preview" button on the **Label layout** (`WHSLabelLayout`) and **Document routing layout** (`WHSDocumentRoutingLayout`) forms. Resolves the layout's ZPL, substitutes `$tokens$` via the standard `WhsDocumentRoutingTranslator` (real record else sample data), renders PNG(s), shows a dialog.
- **Feature B — Mobile-flow capture:** CoC hooks on the `WhsDocumentRouting` print path stage final token-substituted ZPL + Work context into a staging table; a batch renders and attaches PNGs to the **Work** record (DocuType `LBLPREV`). Gated by an auto-expiring WHS parameter that mirrors the *Work creation history log* pattern byte-for-byte.

Locked product decisions (do not revisit): auto-off default **14 days**; render defaults **8 dpmm / 101.6×50.8 mm**, parameter-overridable; multi-label ZPL → **one PNG per `^XA…^XZ` block** with `_{seq}` filename suffix; Work UI = **standard attachments paperclip only**; distribution = **packaged ISV solution**.

## 2. Environment facts

| Item | Value |
|---|---|
| Box | D365 F&O dev VM, Windows Server 2022, PowerShell 5.1 |
| Metadata root | `C:\AOSService\PackagesLocalDirectory` |
| This repo / model folder | `C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools` (git repo, branch `main`) |
| Platform version | 7.0.7996.11 (`ApplicationPlatform\Descriptor`, `bin\AOSKernel.dll`) |
| Build tasks | `Microsoft.Dynamics.Framework.Tools.BuildTasks.17.0.targets` (per sibling `.rnrproj`) |
| X++ compiler | **Verified present:** `C:\AOSService\PackagesLocalDirectory\bin\xppc.exe` |
| Internet / NuGet | Reachable but **flaky DNS** (intermittent resolution failures observed) — wrap restore/NuGet calls in retry loops; hard-offline → HG-4 |
| Sibling ISV models (conventions donors) | `AAXPOCloseHelper`, `AAXDataEntities`, **`AAXIntegrationOperations` (primary donor for net-new elements — §III.1)** |
| SkiaSharp already in AOS | `ElectronicReporting\bin\SkiaSharp.dll` FileVersion **3.119.0.0** + native `libSkiaSharp.dll` (x64) |
| ZXing | **PRESENT in `ElectronicReporting\bin`** (correction 2026-06-04): `zxing.dll` v0.16.5.0 **strong-named** (PKT `4e88037ac681fe60`), `zxing.presentation.dll` v0.16.5.0, `ZXing.Windows.Compatibility.dll` v0.16.6.0. We still ship modern ZXing.Net (≥ 0.16.11, transitive via BinaryKits) — this is a **side-by-side concern, not absence** (F28, DR-1, WP-0.5) |
| Current model state | Only `reference\links.txt` + .md docs. **No Descriptor, no metadata folders — full bootstrap required** |

## 3. Hard constraints

- **C-1** You MUST NOT modify any file outside `C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\`. All Microsoft and sibling-model folders are read-only references.
- **C-2** You MUST NOT call X++ methods marked `internal` from the new model (they are inaccessible cross-model and the compiler will reject them — see F9, F19). `[Hookable(false)]` only forbids CoC, not direct calls; `internal` forbids direct calls from other models.
- **C-3** Every CoC extension MUST: be a `final` class named `<Target>_AAXWarehouseTools_Extension`, carry `[ExtensionOf(...)]`, and call `next ...(...)` exactly once on every code path. You MUST NOT wrap methods marked `[Hookable(false)]`.
- **C-4** All new AOT element names MUST carry the `AAX` prefix (ISV conflict avoidance). New labels MUST live in label file `AAXWarehouseTools_en-US` (`LabelFileId` = `AAXWarehouseTools`) and be referenced as `@AAXWarehouseTools:<Key>` (prefix = LabelFileId, F36). No hard-coded user-facing strings in X++.
- **C-5** Token substitution MUST reuse `WhsDocumentRoutingTranslator` (F6). You MUST NOT reimplement `$token$` parsing for substitution (a read-only regex scan for *discovery* is allowed, §III.8).
- **C-6** Metadata XML MUST be authored by copying the schema of a named donor file (Part III tables) and editing values. You MUST NOT invent element names, ordering, or attributes — the F&O metadata loader is schema-strict and element order matters.
- **C-7** The temporary probe class (WP-0.5) MUST be deleted before final merge.
- **C-8** Git: work on `main` is fine (solo dev box); commit at each WP boundary with message `WP-x.y: <summary>`; do not push unless asked.
- **C-9** Do not send any label/ZPL data to external services (the entire point of this project). Renderer runs in-process.
- **C-10** When a Decision Rule (Part VII) covers an ambiguity, apply it and record the choice in `DECISIONS.md` at repo root. If something material is ambiguous and NOT covered by a Decision Rule, STOP and ask the human — do not guess.
- **C-11** X++ source of truth is the `Ax<Type>\*.xml` files under `<Package>\<ModelFolder>\` in `C:\AOSService\PackagesLocalDirectory`. **`XppMetadata\` folders and `bin\` folders are build output (binaries/generated) — never read them as source evidence and never edit them.** For X++ language semantics, consult the official references: https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/dev-ref/xpp-language-reference and https://learn.microsoft.com/en-us/training/modules/get-started-xpp-finance-operations/.

## 4. Build & verification commands

- **C# build/test:** `dotnet build` / `dotnet test` on the csproj/sln (library + xunit tests). Run from `Projects\AAXWarehouseTools\`.
- **X++ compile:** preferred CLI on a dev box:
  `C:\AOSService\PackagesLocalDirectory\bin\xppc.exe -metadata="C:\AOSService\PackagesLocalDirectory" -compilermetadata="C:\AOSService\PackagesLocalDirectory" -modelmodule=AAXWarehouseTools -output="C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\bin" -referencefolder="C:\AOSService\PackagesLocalDirectory" -log=BuildModelResult.log -xmllog=BuildModelResult.xml`
  Verify `xppc.exe` exists at that path first; if absent, locate it under `PackagesLocalDirectory\bin` or the VS Dynamics tools folder, or build via the VS add-in. A clean compile = zero errors in `BuildModelResult.xml`.
- **DB sync (needed once tables/table-extensions exist):** `SyncEngine.exe` **verified present** at `C:\AOSService\PackagesLocalDirectory\bin\SyncEngine.exe` (also `C:\AOSService\webroot\bin\`).
  `C:\AOSService\PackagesLocalDirectory\bin\SyncEngine.exe -syncmode=fullall -metadatabinaries="C:\AOSService\PackagesLocalDirectory" -connect="<read from C:\AOSService\webroot\web.config DataAccess keys>"`
  If the connection string is not discoverable, mark **HUMAN GATE** (the human runs sync from VS).
- **AOS pickup of new DLLs/metadata:** `iisreset` (or restart the AOS service) after copying DLLs to the model `bin` and after first model creation. If permission is denied, HUMAN GATE.
- **Runnable-class execution for probes:** browser URL `https://<aos>/?cmp=<company>&mi=SysClassRunner&cls=<ClassName>`, or run from VS. If no browser automation is available, HUMAN GATE with exact URL provided.

## 5. Human gates (expected, plan around them)

| Gate | When | What the human does |
|---|---|---|
| HG-1 | WP-0.5 | Run/observe the in-AOS render probe (SysClassRunner URL supplied by you) |
| HG-2 | WP-2 V5/V6 | Execute a mobile LP-print flow on a device/emulator |
| HG-3 | End | Deploy deployable package to Tier-2 sandbox; re-run probe (final R-1 proof) |
| HG-4 | Any | `iisreset`/DB-sync if your process lacks rights |

---

# PART II — Verified codebase facts (evidence base)

Every fact below was verified by direct file reads on this box (2026-06-03). Cite F-numbers in code comments where a design hinges on them.

## II.1 Print path (`ApplicationSuite\Foundation\AxClass\WhsDocumentRouting.xml`)

| # | Fact |
|---|---|
| **F1** | `public void printDocument(WHSWorkTransType _workTransType, WHSLicensePlateLabel _label)` — **line 199**, public, CoC-able. Branches at L205 on `WHSParameters::find().UseWHSLabelLayoutForDocumentRoutingLine`: true → `printDocumentWithDocumentRoutingLabelLayoutLine()` (L221, new path), false → `printDocumentWithDocumentRoutingLine()` (L254, legacy path) |
| **F1a** | **Capture-seam reach — all FOUR LP-label `printDocument` callers on this build:** `WhsLicensePlateLabelBuild` (its L1321 — mobile receiving/put-away LP labels; **sets `label.WorkId` at its L1087** → captured), `whsWorkExecuteDisplayReprintLabel` (L158 — legacy mobile **reprint** flow), `WHSProcessGuideLabelPrintStep` (`ProcessGuide\ProcessGuide\AxClass\`, its L70 — newer **Process Guide** mobile-app reprint step), `ProdLicensePlateLabelBuild` (L87 — production LP labels). All four reach the WP-2.3 instance CoC, but capture is additionally gated on non-empty `_label.WorkId` (REQ-B-5): **production labels are NEVER captured by design** — `ProdLicensePlateLabelBuild` builds the label "without WhsWorkTable" (its class doc; `initLabel` L111-132 never sets WorkId). Both reprint steps select the LP by `order by PrintedDate desc` with **no WorkId filter** (legacy L153-155, Process Guide L65-67) → an empty-WorkId reprint stages nothing (by design) |
| **F2** | `public static void printLabelToPrinter(Name _printerName, str _label)` — **line 349**, `[SysObsolete('…', false, …)]` = soft compile warning only, **NOT** `[Hookable(false)]` → static CoC is legal. Batch diversion L351-357: if `WhsBatchedDocumentRoutingContext::instance()` && `WHSSysCorpNetPrinters::find(_printerName).IsBatchPrintEnabled` → `batchLabel()` + return; else L360 → `printLabelPrintCommandToPrinterWithEncoding()` (UTF8) |
| **F3** | Legacy path (L254): per routing line — translator instantiated with routing line → `this.translate(layout.zpl, _label)` (L276) → **`printLabelToPrinter(printerName, finalStr)`** (L282) |
| **F4** | New path: `WhsLicensePlateLabelPrintCommandGenerator::printLabelsForLicensePlate(...)` (`[Replaceable]`, its L42) → `printLabels()` → `printLabelPrintCommandToPrinter(Name, WhsLabelPrintCommand)` (L379) → switch on `parmLabelType()`: **`WhsLabelPrintCommandType::ZPL` case calls `printLabelToPrinter()` at L391**; `ExternalLabelPrintServiceCommand` case bypasses it (L394-395) |
| **F4a** | The `SysObsolete` successor `printZPLLabelToPrinter(Name, str)` **exists on this build — L369, `[Hookable(false)]`** (cannot be CoC'd; doesn't need to be): its body is `printLabelPrintCommandToPrinter(new WhsLabelPrintCommand(ZPL, _label))` → ZPL case → **routes back into `printLabelToPrinter` (L391). NOT a bypass.** Direct callers: `WHSWaveLabelHistoryPrint` (L267), `WHSPrintLabels` (L700/L1540) — wave-label flows — and `TMSContainerShippingLabelTypeZPL` (L21). These hit our static CoC but with no capture scope open → correctly not staged (out of scope by design) |
| **F5** | **Conclusion: ALL raw-ZPL traffic from both paths funnels through `printLabelToPrinter` *before* the batch diversion.** The batched flush (`WhsBatchedDocumentRoutingContext.printBatchedLabels()`, L123) bypasses it, but per-label `batchLabel()` queueing happens *inside* `printLabelToPrinter` — i.e., after our CoC fires. Batching cannot detach capture |
| **F6** | `WhsDocumentRoutingTranslator` (own file): `public class`, `protected new()`, `[Hookable(false)] public static construct()` (L84). Fluent API, all public and directly callable: `withRecord(Common, TableName = '')` L145 • `withRecordsFromQueryRun(QueryRun)` L169 • `withParameterMap(Map _keyValueMap, str _parameterMapName = 'parameters')` L343 • `withLanguage(LanguageId = '')` L100 • `translate(str) → str` L366 |
| **F7** | Token grammar (translator L16): regex `\$(?:(?<record>[a-zA-Z0-9_]+?)\.)?(?<field>[a-zA-Z0-9_]+?)(?<methodIndicator>\(\))?(?:\[(?<lineIndex>[0-9]{1,3})\])?(?::(?<format>.*?))?\$` — i.e., `$Record.Field()[lineIndex]:format$`; `$$` left untouched |
| **F8** | `WHSLicensePlateLabel` table: `WorkId` (EDT `WHSWorkId`, relation → `WHSWorkTable`), `LicensePlateId`, `WorkTransType`, `LabelId`, `InventLocationId`, `UserId`, `AccountNum`, zone fields |

## II.2 Layout tables / forms (Feature A)

| # | Fact |
|---|---|
| **F9** | ⚠️ `WHSLabelLayout.getLayoutSource()` is **`[Hookable(false)] internal`** (L109), and the Variables generator chain (`WhsLabelLayoutVariablesSourceGenerator.getLayoutSourceFromVariables()`) is also `internal` → **inaccessible from our model (C-2)**. ZPL resolution must be replicated per §V WP-1.3 |
| **F10** | `WHSLabelLayout` fields (9): `LabelLayoutId`, `LayoutType`, `Description`, `LabelLayoutDataSource` (FK), `ActiveVersion` (RefRecId → **`WHSLabelLayoutVersion`** — the ZPL lives there, versioned), `EnableTemplateTranslator`, `LabelLocale`, `DefinitionType` (enum `WHSLabelLayoutDefinitionType`: **ZPL=0 default, Variables=1, VariablesScript=2**), `PrinterStockTypeName`. **No width/height/density fields** |
| **F11** | ZPL-type resolution replica: `select firstonly ZPL from WHSLabelLayoutVersion where RecId == labelLayout.ActiveVersion` (tables are public) |
| **F12** | `WHSLabelLayoutDataSource` fields: `LayoutType`, `LabelLayoutDataSourceId`, `Description`, **`DataSourceQuery` (container, EDT `PackedQueryRun`)**, `CustomLabelRootDataSourceTable`, `JoinType` |
| **F13** | `WHSDocumentRoutingLayout` fields (4): `LayoutId`, `Description`, **`ZPL` (EDT `WHSZPL`, StringSize −1, directly on the table)**, `PrinterStockTypeName`. **No size fields** |
| **F14** | Both forms = **SimpleListDetails** pattern with **empty `ActionPane` (`<Controls />`)** — clean extension target. `WHSDocumentRoutingLayout` form has an `InsertButton` that pastes `$Field$` tokens |
| **F15** | Display menu items exist for both forms, ConfigurationKey `WHSandTMS`, standard licensing (no bespoke privileges) |

## II.3 Parameters / auto-off pattern (`AxTable\WHSParameters.xml`, `AxForm\WHSParameters.xml`)

| # | Fact |
|---|---|
| **F16** | Fields: `WorkCreateHistoryLog` (NoYes), `WorkCreateHistoryLogUntilDate` (utcdatetime, `AllowEdit=No`, `AllowEditOnCreate=No`) — table XML L2738-2750 |
| **F17** | `[Hookable(false)] internal void initWorkCreateHistoryLogUntilDate()` (L283): `UntilDate = utcNow + duration`. Duration is a **hard-coded `protected` method returning 7**. (Ours is a configurable field, default 14 — deliberate divergence) |
| **F18** | `isWorkCreateHistoryLogEnabled()` (L669) — THE pattern to mirror: toggle on && `utcNow < UntilDate` → true. Else `mustEnable = (UntilDate == utcDateTimeNull())` (first-enable bootstrap); under guard: `readPast(true)`, `ttsbegin`, **`select pessimisticlock firstonly`** keyed on `ParametersKey` + *current* UntilDate + toggle still true (re-check defeats races), then `init…()` if mustEnable else **flip toggle off**, `update()`, `ttscommit`; return `mustEnable` |
| **F19** | `canUpdateWHSParameters()` is `[Wrappable(true)] internal`, body `return true;` (test seam) → replicate privately, do not call |
| **F20** | `modifiedField` (L296): `case fieldNum(WHSParameters, WorkCreateHistoryLog): if (on) initWorkCreateHistoryLogUntilDate();` |
| **F21** | Hot-path guard exemplar: `WHSWorkCreateHistory::createWorkHistoryLineFromTmpWorkLine()` checks `WHSParameters::find().isWorkCreateHistoryLogEnabled()` then inserts on a separate `UserConnection` |
| **F22** | Form placement: **"Work creation" tab** → group **`WorkCreateHistoryLogExpirable`** (caption `@WAX1710`, `Breakable=No`): checkbox bound `WorkCreateHistoryLog` + read-only DateTime bound `WorkCreateHistoryLogUntilDate` (form XML L2511-2557) |
| **F23** | `WHSWorkCreateHistoryPurge`: `extends RunBaseBatch implements BatchRetryable, SysErrorMessageHelpScopeIBatchProvider`; `run()` wraps cleanup in `#OCCRetryCount` retry (Deadlock / TransientSqlConnectionError / UpdateConflict); `cleanup()` = `using (var committer = WHSRecordDeletionCommitter::construct()) { while select forupdate … committer.deleteRecord(buf); }`; action menu item, ConfigurationKey `WHSandTMS` |
| **F24** | Table-extension donor exists: `WarehouseOrders\WarehouseOrders\AxTableExtension\WHSParameters.WarehouseOrders.xml` |

## II.4 Attachment / file APIs

| # | Fact |
|---|---|
| **F25** | `public static DocuRef attachFileForRecord(Common _refRec, DocuTypeId _type, System.IO.Stream _file, str _fileName, str _attachmentName, str _notes = '')` (`ApplicationFoundation\…\AxClass\DocumentManagement.xml`). Core `attachFile()` validates `SysDictClass::isEqualOrSuperclass(docuType.ActionClassId, classNum(DocuActionFile))` — DocuType without correct ActionClassId throws. Also: `attachFileWithRetention(... int _retentionDays ...)` (0 or 1–14600) |
| **F26** | DocuType creation: set `TypeId`, `Name`, `TypeGroup = DocuTypeGroup::File`, `FilePlace = DocuFilePlace::Archive`, **`ActionClassId = classNum(DocuActionArchive)`** — CORRECTED 2026-06-04 live: `DocuActionFile` is **abstract** and `DocuType.insert()` instantiates the action class ("Instances of abstract classes cannot be created"); `DocuActionArchive` is the concrete `final` subclass (MS precedent: `DocuUpload`) and still satisfies `attachFile()`'s `isEqualOrSuperclass(_, DocuActionFile)` validation. DocuType is **per-company** |
| **F27** | `File::SendFileToTempStore(System.IO.Stream stream, str fileName, classname strategy = classstr(FileUploadTemporaryStorageStrategy), boolean _downloadOnly = false) → str` (URL, empty on failure). **Stream-only; no container overload** |

## II.5 Model & runtime

| # | Fact |
|---|---|
| **F28** | `ElectronicReporting\bin`: SkiaSharp.dll **3.119.0.0** + native libSkiaSharp.dll, **AND ZXing**: `zxing` **v0.16.5.0 strong-named (PKT `4e88037ac681fe60`)** + `zxing.presentation` 0.16.5.0 + `ZXing.Windows.Compatibility` 0.16.6.0. Modern ZXing.Net (≥ 0.16.11, F34) ships assembly name `ZXing` — identity differs from ER's legacy `zxing`, so they MAY bind independently, but this is **unproven side-by-side** (same risk class as SkiaSharp, U-2) — the WP-0.5 probe arbitrates both |
| **F29** | AxReference donor with **`PublicKeyToken=null`** works on this box: `AAXIntegrationOperations\AAXIntegrationOperations\AxReference\AAXFileIngestion.xml`. Schema elements: `Name`, `AssemblyName`, `AssemblyDisplayName`, optional `PublicKeyToken`, `Version` |
| **F30** | Descriptor donor: `AAXPOCloseHelper\Descriptor\AAXPOCloseHelper.xml` — Layer 14, Publisher `www.atomicax.com`, Id 895972602 (AAXDataEntities = 895972580), `Customization=Allow`, version 1.0.0.0 |
| **F31** | `.rnrproj` donor: `AAXPOCloseHelper\Projects\AAXPOCloseHelper\AAXPOCloseHelper\AAXPOCloseHelper.rnrproj` (ToolsVersion 14.0, `<Model>` name, `TargetFrameworkVersion v4.6`, `OutputPath bin`, BuildTasks 17.0 import). `.sln` donor mixing rnrproj + net8.0 csprojs: `AAXDataEntities\Projects\AAXDataEntities\AAXDataEntities.sln` |
| **F32** | `AAXWarehouseTools` has zero model structure today — full bootstrap |
| **F33** | Test-project convention donor: `AAXDataEntities\tests\*` (net8.0 xunit beside the model) |
| **F34** | **BinaryKits.Zpl.Viewer 1.3.1** (latest on nuget.org, nuspec verified 2026-06-04): TFMs **net472 / net8.0 / netstandard2.0**; deps `BinaryKits.Zpl.Label >= 3.3.1`, `SkiaSharp.HarfBuzz >= 3.119.1`, `HarfBuzzSharp.NativeAssets.Linux >= 8.3.1.2`, `SkiaSharp.NativeAssets.Linux.NoDependencies >= 3.119.1` (nuspec lists Linux natives; **Windows natives come via `SkiaSharp.NativeAssets.Win32` / `HarfBuzzSharp.NativeAssets.Win32` — add explicitly**), `ZXing.Net >= 0.16.11`. **SkiaSharp 3.119.x = same line as ER's 3.119.0.0 (F28) — version-collision risk largely pre-resolved.** NEW: HarfBuzz managed + native DLLs are additional payload |
| **F35** | `AAXIntegrationOperations` C# precedent: **old-style csproj, `TargetFrameworkVersion v4.7.2` (net472), unsigned (PKT=null), loads in this AOS from the model `bin\` today** — proven runway for our TFM/signing choices. Sibling has no post-build copy (manual); we automate ours. Build = VS/msbuild on the `.sln`; expect benign warning classes (pattern-missing forms, InternalUseOnly references, >900-byte index) |
| **F36** | Label-file mechanics (verified in `AAXIntegrationOperations`): descriptor `AxLabelFile\<Model>_en-US.xml` whose **`LabelFileId` defines the `@<LabelFileId>:Key` reference prefix**; content file is named **`<Model>.en-US.label.txt` (dot, not underscore)** at `AxLabelFile\LabelResources\en-US\`; format `Key=Text` with optional ` ;comment` continuation lines |

## II.6 Scope clarifications

- `ExternalLabelPrintServiceCommand` labels (F4) are not raw ZPL → **not captured** (document in parameter help text).
- Wave-label printing and ER-format labels are different paths → out of scope.
- The `SysObsolete` successor `printZPLLabelToPrinter` exists on this build but **routes back through `printLabelToPrinter`** (F4a) — the funnel holds. Its direct callers are wave-label/TMS flows, correctly excluded by the scope gate. Seam smoke test (WP-3.4) guards future drift.
- **Production LP labels (`ProdLicensePlateLabelBuild`) are never captured:** the label is built "without WhsWorkTable" and carries no `WorkId` → no attachment target → REQ-B-5 gate never opens a scope (F1a). Document in parameter help text alongside the ZPL-only note.

---

# PART III — Methodologies & patterns (HOW to build each artifact)

## III.1 Golden rule for metadata XML

**Before creating any artifact of type T, read the donor file for T listed below, copy its full XML structure, and edit values only.** Element order is significant. Donor table:

| Artifact type | Donor to copy schema from |
|---|---|
| Model descriptor | `AAXPOCloseHelper\Descriptor\AAXPOCloseHelper.xml` (F30); cross-check `AAXIntegrationOperations\Descriptor\AAXIntegrationOperations.xml` (Id 896000928) |
| `.rnrproj` / `.sln` | F31 donors; `AAXIntegrationOperations\Projects\…` shows the 4-project sln shape (model + C# libs + C# tests) |
| AxClass | `AAXIntegrationOperations\AAXIntegrationOperations\AxClass\AAXAsyncOperationTypeRegistration.xml` — small, complete envelope (`AxClass` → `SourceCode` → `Declaration` CDATA → `Methods`/`Method`/`Name`+`Source` CDATA) |
| AxTable (new) | `AAXIntegrationOperations\…\AxTable\AAXAsyncOperationTracking.xml` — complete net-new table: Declaration, static find/exist methods, element order (`Label`→`TableGroup`→`TitleField*`→`ClusteredIndex`→`ReplacementKey`→`SaveDataPerCompany`→`DeleteActions`→`FieldGroups`→`Fields` (typed via `i:type="AxTableFieldString/Enum/Guid/Int64/UtcDateTime"`)→`FullTextIndexes`→`Indexes` (incl. `AlternateKey`)→`Mappings`→`Relations`→`StateMachines`) |
| AxTableExtension | `WarehouseOrders\WarehouseOrders\AxTableExtension\WHSParameters.WarehouseOrders.xml` (F24 — same target table!) |
| AxFormExtension | Search `PackagesLocalDirectory\*\*\AxFormExtension\*.xml` for one adding an ActionPane button; prefer a WHS-area example |
| AxForm (dialog) | `AAXIntegrationOperations\…\AxForm\AAXLiquidTester.xml` — small Custom-pattern form with ActionPane + button-clicked handler + **CLR interop call from a form method** (exactly our dialog's shape); for Dialog styling also grep `<Style>Dialog</Style>` in ApplicationSuite |
| AxEnum | `AAXIntegrationOperations\…\AxEnum\AAXAsyncOperationStatus.xml` (status enum with labels — near-identical to our `AAXWHSLabelCaptureStatus`) |
| AxEdt | None in AAXIntegrationOperations → use an ApplicationSuite WHS EDT of the same base type (e.g. `AxEdt\WHSZPL.xml` for string) |
| AxMenuItemAction / Display | `AAXIntegrationOperations\…\AxMenuItemAction\AAXServiceMetadataRefresh.xml` / `AxMenuItemDisplay\AAXServiceMetadataExplorer.xml`; WHS batch example: `ApplicationSuite\Foundation\AxMenuItemAction\WHSWorkCreateHistoryPurge.xml` |
| AxMenuExtension | Search for an existing extension of the Warehouse management menu in any non-MS model; else any AxMenuExtension donor |
| AxSecurityPrivilege / Duty | `AAXIntegrationOperations\…\AxSecurityPrivilege\AAXAsyncOperationServiceMaintain.xml` (full EntryPoints schema — note ours use `ObjectType` `MenuItemAction`/`MenuItemDisplay`, not `ServiceOperation`) / `AxSecurityDuty\AAXIntegrationServiceOperationsDuty.xml` |
| AxLabelFile | `AAXIntegrationOperations\…\AxLabelFile\AAXIntegrationOperations_en-US.xml` + `LabelResources\en-US\AAXIntegrationOperations.en-US.label.txt` (F36 naming!) |
| AxReference | `AAXIntegrationOperations\AAXIntegrationOperations\AxReference\AAXFileIngestion.xml` (F29) |
| C# csproj for AOS-loaded lib | `AAXIntegrationOperations\Tools\AAXFileIngestion\AAXFileIngestion.csproj` (net472, old-style, proven in-AOS — F35) |

## III.2 X++ source inside metadata XML

X++ code lives in `<Source><![CDATA[ … ]]></Source>` blocks per method, inside `<Method>` elements under `<Methods>`, with a `<Declaration>` CDATA for the class declaration. Match the donor exactly (indentation: 4 spaces; method `<Name>` element must equal the X++ method name).

## III.3 Chain of Command pattern (the only extension mechanism used here)

```xpp
[ExtensionOf(classStr(WhsDocumentRouting))]            // or tableStr(...) / formStr(...)
public final class WhsDocumentRouting_AAXWarehouseTools_Extension
{
    public void printDocument(WHSWorkTransType _workTransType, WHSLicensePlateLabel _label)
    {
        // pre-logic
        next printDocument(_workTransType, _label);     // MANDATORY, exactly once, all paths
        // post-logic
    }
    public static void printLabelToPrinter(Name _printerName, str _label)   // static CoC: same shape
    {
        next printLabelToPrinter(_printerName, _label);
    }
}
```
Rules: class is `final`; suffix `_AAXWarehouseTools_Extension`; one extension class per target; cannot wrap `[Hookable(false)]` methods; `[SysObsolete(..., false, ...)]` targets are legal to wrap (warning only — acceptable, F2).

## III.4 Table-extension pattern

New fields on `WHSParameters` go in `AxTableExtension\WHSParameters.AAXWarehouseTools.xml` (naming: `<Table>.<Model>.xml`). New *methods* for a table go in a separate `[ExtensionOf(tableStr(...))]` class (III.3) — metadata extensions carry fields/indexes only.

## III.5 SysOperation pattern (render batch)

Three classes: `*Contract` (`[DataContract]`, `[DataMember]` parm methods), `*Service` (extends `SysOperationServiceBase`, public `process(Contract)` method), `*Controller` (extends `SysOperationServiceController`, `main()`, `new()` wiring `classStr(Service), methodStr(Service, process)`). Mark service operations batch-retryable: implement `BatchRetryable` → `isRetryable() { return true; }` on the controller. Donor: search ApplicationSuite for a small SysOperation trio (e.g. grep `extends SysOperationServiceController` and pick a WHS one).

## III.6 RunBaseBatch purge pattern (copy F23 verbatim)

`AAXWHSLabelCapturePurge extends RunBaseBatch implements BatchRetryable`: `run()` = `#OCCRetryCount` + try/catch Deadlock→`retry`, TransientSqlConnectionError→helper-gated `retry`, UpdateConflict→retry-count-gated `retry`; `cleanup()` = `using (var committer = WHSRecordDeletionCommitter::construct()) { while select forupdate … committer.deleteRecord(buf); }`. Plus `pack/unpack`, `canGoBatchJournal`, `description()`, `main()`, dialog for parameters. Donor: `ApplicationSuite\Foundation\AxClass\WHSWorkCreateHistoryPurge.xml` — copy structure, change query + dialog fields.

## III.7 X++ ↔ .NET interop pattern

```xpp
System.Collections.Generic.IList<System.Byte[]> clrList =
    AtomicAx.Zpl.Render.ZplRenderService::RenderToPngList(_zpl, _dpmm, _widthMm, _heightMm);
// Byte[] -> container:
System.Byte[] bytes = clrList.get_Item(i);
container c = Binary::constructFromMemoryStream(new System.IO.MemoryStream(bytes)).getContainer();
// container -> Stream (for DocumentManagement / SendFileToTempStore, F25/F27):
Binary b = Binary::constructFromContainer(c);
System.IO.MemoryStream stream = b.getMemoryStream();
```
Wrap CLR calls in try/catch with `CLRErrors`/`System.Exception` unwrap (walk `InnerException`) and surface readable `error()` text. Generic CLR types and exact conversion calls MUST be validated at compile time; if `Binary` lacks a used method on this version, fall back to `new System.IO.MemoryStream(bytes)` directly where a Stream is needed and container conversion via `BinHelper`/`Binary` equivalents (DR-7).

## III.8 Token discovery (read-only scan, allowed by C-5)

For the seed dialog & sample fallback, enumerate tokens with our own copy of the verified regex (F7) via `System.Text.RegularExpressions.Regex` in X++ (or in the C# lib as a helper `GetTokenRecordNames(zpl)` — preferred, more testable). Substitution itself remains the translator's job (C-5).

## III.9 Hash pattern

`ZplHash = SHA-256 hex of the final ZPL string (UTF-8)`. Implement in the C# library (`ZplRenderService.ComputeHash(string) → string`) so X++ and tests share one implementation.

## III.10 Label-file references (F36)

Every `Label`, `HelpText`, dialog caption etc. in metadata XML uses `@AAXWarehouseTools:Key` (prefix = `LabelFileId`). Files: descriptor `AxLabelFile\AAXWarehouseTools_en-US.xml` (with `LabelFileId` = `AAXWarehouseTools`, `LabelContentFileName` = `AAXWarehouseTools.en-US.label.txt`) + content `AxLabelFile\LabelResources\en-US\AAXWarehouseTools.en-US.label.txt` (**dot before en-US, not underscore** — F36) with `Key=English text` lines and optional ` ;comment` continuation lines. Create in WP-0.1 so all later WPs can reference it.

---

# PART IV — Requirements catalog

## IV.1 Feature A (preview)

- **REQ-A-1** Button `AAXGenerateLabelPreview` on the ActionPane of forms `WHSLabelLayout` and `WHSDocumentRoutingLayout` (form extensions; empty ActionPane confirmed F14). Visible/enabled only with privilege `AAXWHSLabelPreviewView`.
- **REQ-A-2** ZPL resolution: `WHSDocumentRoutingLayout` → `ZPL` field (F13; this table has no template/variables machinery). `WHSLabelLayout` — check **`EnableTemplateTranslator` FIRST (it is orthogonal to `DefinitionType`** — separate field, enabled for any layout with a data source): if `Yes` → info `@AAXWarehouseTools:PreviewTemplateNotSupported` and stop. (The production path — `WhsLabelPrintCommandGenerator.printLabels` L82 — expands `{{Header}}/{{Row …}}/{{Footer}}` blocks via `WhsDocumentRoutingTemplateTranslator` over a data-source query; that `{{…}}` grammar is distinct from the `$token$` grammar (F7) and is NOT handled by `WhsDocumentRoutingTranslator` — feeding it through would preview garbage. v2 backlog.) Else: `DefinitionType::ZPL` → active version's ZPL (F11); no active version → info `@AAXWarehouseTools:NoActiveVersion`. `DefinitionType::Variables/VariablesScript` → info `@AAXWarehouseTools:PreviewZplOnly` and stop (F9 — internal API; v2 backlog).
- **REQ-A-3** Token data resolution order: (1) optional seed dialog — one selector per bound data source, pre-populated with most-recent record (RecId desc); (2) skipped → auto-resolve most-recent; (3) unresolvable/empty → sample map. Data sources derived from `WHSLabelLayoutDataSource.DataSourceQuery` (unpacked `PackedQueryRun`, F12) for label layouts with a data source, else `WHSLicensePlateLabel` (legacy/document-routing). DR-3 covers unpack failure.
- **REQ-A-4** Substitution exclusively via `WhsDocumentRoutingTranslator::construct()` fluent chain incl. `withLanguage(layout.LabelLocale)` where the field exists (F6, C-5). Sample values via `withParameterMap(sampleMap)` (note: map name defaults to `'parameters'`).
- **REQ-A-5** Sample map (class `AAXWHSLabelPreviewSampleData`) with realistic-length values (`PO123456`, `LP000123`, item `A0001`, qty `12`, dates = today via `systemDateGet()`), maintainable in one place.
- **REQ-A-6** Render via `AAXZplRenderService` (REQ-R-*). Multi-label → dialog pages through images ("`@AAXWarehouseTools:LabelNofM`" = "Label %1 of %2").
- **REQ-A-7** Dialog `AAXWHSLabelPreviewDialog`: image control bound to a temp-store URL. **`File::SendFileToTempStore` is stream-only and returns ONE URL per call (F27) — N label blocks ⇒ N separate uploads ⇒ N URLs**; prev/next swaps the image control to the current page's URL. Prefer generating each page's URL **lazily on navigation** (temp-store URLs have a limited server-side lifetime — avoids stale-URL 404s in a long-open dialog); if generating all up front, document the short-lived-dialog assumption. Collapsible read-only final-ZPL text; **Download PNG** (current page's URL) and **Copy ZPL** affordances; badge "Live record: %1" / "Sample data" per source.
- **REQ-A-8** Render/parse failure → `error()` with renderer message + offending ZPL displayed inline (never a silent or generic failure).

## IV.2 Feature B (capture)

- **REQ-B-1** WHSParameters extension fields (F24 donor, defaults locked): `AAXPreviewToWorkAttachment` (NoYes, No), `AAXPreviewAttachmentTurnsOff` (utcdatetime, AllowEdit=No + AllowEditOnCreate=No, mirroring F16), `AAXPreviewAutoOffDays` (int, 14), `AAXPreviewDefaultDpmm` (int, 8), `AAXPreviewDefaultWidthMm` (real, 101.6), `AAXPreviewDefaultHeightMm` (real, 50.8), `AAXPreviewRetentionDays` (int, 30).
- **REQ-B-2** Auto-off logic mirrors F18 **exactly** (incl. `readPast(true)`, `pessimisticlock` keyed re-check, `mustEnable` null-date bootstrap, flip-off + return semantics) — see skeleton in WP-2.1. Private `aaxCanUpdateWHSParameters()` replica of F19.
- **REQ-B-3** `modifiedField` CoC: toggle flipped On → `aaxInitPreviewAttachmentTurnsOff()` (mirror F20).
- **REQ-B-4** Parameters form extension: group `@AAXWarehouseTools:ParamGroupLabelPreview` on the **"Work creation" tab adjacent to group `WorkCreateHistoryLogExpirable`** (verified F22), control types mirroring F22 (checkbox + read-only DateTime) + the numeric fields.
- **REQ-B-5** Capture seams (F1–F5, coverage F1a): CoC on `printDocument` opens disposable `AAXWHSLabelCaptureScope` (WorkId, LicensePlateId, WorkTransType, per-scope staged-hash `Set`) ONLY when `aaxIsPreviewToWorkAttachmentEnabled()` (cheap gate first — zero overhead when off) **AND `_label.WorkId` is non-empty** (no attachment target otherwise — don't open a scope at all; verify population per flow in V5, U-7); CoC on static `printLabelToPrinter` stages one row when a scope is active. `using`-disposal mandatory (exception safety).
- **REQ-B-6** Staging table `AAXWHSLabelCaptureStaging` per WP-2.2 schema; insert is a single row in the caller's transaction (DR-6 documents the UserConnection alternative).
- **REQ-B-7** Render batch (SysOperation, III.5): gate re-check (`aaxIsPreviewToWorkAttachmentEnabled()` — flips expired toggle off, F18 batch side) + `ensureDocuType()`; pickup `Pending` ordered RecId, chunked (contract param, default 50); `#OCCRetryCount` retry on state transitions. Per row, in order: (1) **resolve the attachment target first**: `WHSWorkTable::find(staging.WorkId)` returns an **empty buffer** (no throw) for a missing WorkId — if empty (Work completed/cancelled/purged between capture and render), mark the row terminal **`Done`** with note `@AAXWarehouseTools:WorkNoLongerExists` + `ProcessedDateTime` and continue — MUST NOT go `Error`/`Poison` (passing an empty buffer to `attachFileForRecord` throws `missingParameter`, F25 — a legitimate transient condition must not poison, DR-10); (2) dedup on `(WorkId, ZplHash)` + existing `LBLPREV` DocuRef notes-hash; (3) render one PNG per label block; (4) attach all; (5) `Done`. **Each row is processed in its own try/catch** — a failure updates only that row's `Status`/`RetryCount`/`ErrorMessage`, never aborts or rolls back the rest of the chunk (do NOT copy F23's whole-block OCC wrap as a chunk transaction; apply OCC retry to per-row state transitions only). Failure → `Error` + `RetryCount++`; ≥ max (contract, default 3) → `Poison`. **Company scope:** staging is per-company (`SaveDataPerCompany=Yes` default); the batch picks up rows `crossCompany` and wraps per-company chunks in `changecompany` so one scheduled job covers all legal entities (DR-9).
- **REQ-B-8** Attach helper: `DocumentManagement::attachFileForRecord(workTable, 'LBLPREV', stream, fileName, name, notesWithHash)` (F25); filename `{WorkId}_{LayoutId}_{yyyyMMdd_HHmmss}_{seq}.png`; `ensureDocuType()` idempotent per company with `ActionClassId = classNum(DocuActionFile)` (F26 — mandatory or attach throws).
- **REQ-B-9** Captured scope limitation documented: ZPL-type labels only; `ExternalLabelPrintServiceCommand` not captured (II.6).

## IV.3 Renderer (shared)

- **REQ-R-1** C# `AtomicAx.Zpl.Render.ZplRenderService.RenderToPngList(string zpl, int dpmm = 0, double widthMm = 0, double heightMm = 0) → IList<byte[]>` — one PNG per `^XA…^XZ`; zeros → parse `^PW` (width dots) / `^LL` (length dots), convert via dpmm; absent → throw `ZplDimensionsMissingException` (X++ owns fallback). Invalid ZPL → `ZplRenderException` with analyzer detail. Internally `ZplAnalyzer.Analyze` → per `LabelInfo` → `ZplElementDrawer.Draw`.
- **REQ-R-2** X++ `AAXZplRenderService::renderToPngList(str, int = 0, real = 0, real = 0) → List` (of container): catches `ZplDimensionsMissingException` → re-invoke with parameter fallbacks (`AAXPreviewDefaultDpmm/WidthMm/HeightMm`); unwraps CLR exceptions (III.7).
- **REQ-R-3** Library version selection per DR-1 (SkiaSharp 3.119 alignment); ZXing.Net shipped (note: ER already loads a strong-named legacy `zxing` v0.16.5.0 in-process — verify side-by-side identity per DR-1/WP-0.5, F28); all DLLs post-build-copied to model `bin\`; AxReference for `AtomicAx.Zpl.Render` only (F29 schema).
- **REQ-R-4** Strong-name with repo-committed `AtomicAx.snk`; DR-2 fallback.
- **REQ-R-5** xunit tests (F33 layout): valid 4×2 render; multi-label count; missing-dims exception; explicit dims override; invalid-ZPL exception; barcode `^BC` renders (ZXing wiring); hash helper stability.

## IV.4 Security / labels / hygiene

- **REQ-S-1** Privileges: `AAXWHSLabelPreviewView` (action menu item for preview), `AAXWHSLabelCaptureAdminister` (render + purge menu items; parameters maintain). Duty `AAXWHSLabelPreviewMaintain` bundles both.
- **REQ-S-2** Menu extension: render batch + purge action menu items under Warehouse management → Periodic tasks; ConfigurationKey `WHSandTMS` on all new menu items (match hosts, F15).
- **REQ-S-3** All user-facing strings via `@AAXWarehouseTools:` labels (C-4).
- **REQ-S-4** `THIRD-PARTY-NOTICES.md` at repo root: BinaryKits.Zpl (MIT), SkiaSharp (MIT), ZXing.Net (Apache-2.0).
- **REQ-S-5** Purge batch `AAXWHSLabelCapturePurge` (III.6): deletes `LBLPREV` DocuRef (cascade DocuValue via `DocuRef.delete()`) older than `AAXPreviewRetentionDays`, plus `Done`/`Poison` staging rows older than same window. Opt-in.

---

# PART V — Work packages (execute in order; ‖ = parallelizable)

## WP-0 — Bootstrap & de-risk *(gates everything)*

### WP-0.1 Model bootstrap
**Read first:** F30/F31 donors.
**Create:**
1. `Descriptor\AAXWarehouseTools.xml` — donor copy; edits: `Id` = **896000929** (adjacent to AAXIntegrationOperations' 896000928; verified free 2026-06-04 — **do NOT use 895972610: it already belongs to Microsoft's HRPlatform model**). Model Id MUST be deployment-unique: before writing the Descriptor, grep every `C:\AOSService\PackagesLocalDirectory\*\Descriptor\*.xml` for `<Id>` and confirm the chosen value appears in none; if taken, pick another unused value in the AAX bands (8959726xx / 89600xxxx) and record the choice in `DECISIONS.md`. `DisplayName/ModelModule/Name` = `AAXWarehouseTools`, `Description` = "ZPL label preview and capture tools for Warehouse management.", `ModuleReferences` = `ApplicationCommon, ApplicationFoundation, ApplicationPlatform, ApplicationSuite, Directory, SourceDocumentation, SourceDocumentationTypes`.
2. Source root `AAXWarehouseTools\AAXWarehouseTools\` (folders per artifact created lazily).
3. `Projects\AAXWarehouseTools\AAXWarehouseTools.sln` + `AAXWarehouseTools\AAXWarehouseTools.rnrproj` (donor copy; `<Model>AAXWarehouseTools</Model>`, fresh GUID, `DBSyncInBuild=True`).
4. Label file: `AxLabelFile\AAXWarehouseTools_en-US.xml` + `AxLabelFile\LabelResources\en-US\AAXWarehouseTools.en-US.label.txt` (donor + naming rules: F36).
5. `THIRD-PARTY-NOTICES.md`, `DECISIONS.md` (empty scaffold).

**Accept:** X++ compile (Part I §4) of the empty model is clean; model visible in AOT after iisreset (HG-4 if needed).

### WP-0.2 C# library `AtomicAx.Zpl.Render`
**Create:** `Projects\AAXWarehouseTools\AtomicAx.Zpl.Render\AtomicAx.Zpl.Render.csproj` + sources.
- TFM: **net472** (proven in-AOS sibling precedent, F35; BinaryKits 1.3.1 ships a net472 build, F34). `netstandard2.0` is the sanctioned alternate (DR-4). SDK-style csproj is fine as long as output is net472.
- Strong-named (`AtomicAx.snk` committed; DR-2 fallback to unsigned per F29/F35 precedent).
- NuGet (pin): **`BinaryKits.Zpl.Viewer 1.3.1`** (F34 — SkiaSharp 3.119.1 line matches ER's 3.119.0) + **`SkiaSharp.NativeAssets.Win32`** + **`HarfBuzzSharp.NativeAssets.Win32`** (nuspec only declares Linux natives — Windows natives must be added explicitly, F34). ZXing.Net comes transitively.
- Public surface: `ZplRenderService.RenderToPngList(...)` (REQ-R-1), `ZplRenderService.ComputeHash(string)` (III.9), `GetTokenRecordNames(string)` (III.8, optional helper), exception types `ZplRenderException` / `ZplDimensionsMissingException`.
- Post-build target: copy `AtomicAx.Zpl.Render.dll`, `BinaryKits.Zpl.Viewer.dll`, `BinaryKits.Zpl.Label.dll`, `SkiaSharp.dll`, `SkiaSharp.HarfBuzz.dll`, `HarfBuzzSharp.dll`, **`libSkiaSharp.dll`** (win-x64), **`libHarfBuzzSharp.dll`** (win-x64), `zxing*.dll` → `C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\bin\`.
- NuGet restore: use retry loops (flaky DNS on this box — Part I §2).

**Accept:** `dotnet build` clean; full DLL set (managed + both natives) lands in model bin.

### WP-0.3 Tests
**Create:** `tests\AtomicAx.Zpl.Render.Tests\` (net8.0 xunit, F33) covering REQ-R-5. **Accept:** `dotnet test` green.

### WP-0.4 AxReference
**Create:** `AAXWarehouseTools\AAXWarehouseTools\AxReference\AtomicAx.Zpl.Render.xml` — F29 schema, real `PublicKeyToken` from the .snk (`sn -T` on the built DLL), Version 1.0.0.0. Transitive deps get **no** AxReference (ER precedent). **Accept:** model still compiles; a trivial X++ class referencing the namespace compiles.

### WP-0.5 In-AOS render probe — **HG-1**
**Create (temporary, C-7):** `AxClass\AAXZplRenderProbe.xml` — runnable (`main`):
1. Render a hardcoded valid 4×2 ZPL via `AAXZplRenderService` (build the minimal wrapper now — it is WP-1.1, pull it forward); `info()` byte counts.
2. Render a **second hardcoded ZPL containing a `^BC` Code-128 barcode** — exercises the ZXing path **in-AOS** (the xunit `^BC` test runs outside the AOS process and proves nothing about co-load).
3. Attach a PNG to any scratch record via `attachFileForRecord` — **reuse the WP-2.5 `ensureDocuType()` pattern (pull it forward too)**: the DocuType MUST have `ActionClassId = classNum(DocuActionFile)` and is per-company (F25/F26 — without it, `attachFile()` throws `Docu_IncorrectDocuType`).
4. **ER-coexistence step:** log the loaded assembly identities — SkiaSharp via `typeof(SkiaSharp.SKBitmap).Assembly`, ZXing via `typeof(ZXing.BarcodeWriterPixelData).Assembly` (name + version + PKT) — ideally after triggering an ER export in the same AOS session.
**Accept (gate):** probe runs in AOS without assembly-load errors; both PNGs render (plain + barcode); PNG attached; SkiaSharp + ZXing identities logged; decisions recorded in `DECISIONS.md`. Record exit in commit `WP-0.5: probe green`.

## WP-1 — Feature A *(after WP-0; ‖ with WP-2.1/2.2)*

### WP-1.1 `AxClass\AAXZplRenderService.xml` — REQ-R-2 (skeleton in III.7; param fallbacks read the WP-2.1 fields — until those exist, use literal defaults 8/101.6/50.8 with a `// TODO WP-2.1` marker, then switch).
### WP-1.2 Form extensions — REQ-A-1: `AxFormExtension\WHSLabelLayout.AAXWarehouseTools.xml`, `AxFormExtension\WHSDocumentRoutingLayout.AAXWarehouseTools.xml` (ActionPane tab + button group + menu-item button bound to `AAXWHSLabelPreview` action menu item; pass current record via `Args.record()`).
### WP-1.3 `AxClass\AAXWHSLabelPreviewController.xml` — orchestrates: resolve ZPL (REQ-A-2 — per-table replica, cite F9/F11/F13 in comments) → context (WP-1.4) → translate (REQ-A-4) → render → dialog.
### WP-1.4 `AxClass\AAXWHSLabelPreviewContext.xml` + `AAXWHSLabelPreviewSampleData.xml` — REQ-A-3/-5; live-vs-sample tracking per source.
### WP-1.5 `AxForm\AAXWHSLabelPreviewDialog.xml` — REQ-A-6/-7/-8. Dialog-style form; image control's URL set from `SendFileToTempStore`; prev/next buttons; collapsible ZPL group. **Accept:** multi-label uses one `SendFileToTempStore` upload per label block (N PNGs ⇒ N URLs — the API is single-stream, F27); paging swaps to the correct per-page URL, generated lazily on navigation; pages opened after a delay still render (no expired-URL 404).
### WP-1.6 `AxMenuItemAction\AAXWHSLabelPreview.xml` (object = controller class, ConfigurationKey `WHSandTMS`) + `AxSecurityPrivilege\AAXWHSLabelPreviewView.xml`.

**WP-1 accept:** compile clean; manual smoke V4 (Part VI) executable on this box.

## WP-2 — Feature B *(2.1‖2.2 first, then 2.3 → 2.4 → 2.5 → 2.6)*

### WP-2.1 Parameters + auto-off — REQ-B-1..4
Files: `AxTableExtension\WHSParameters.AAXWarehouseTools.xml` (fields, F24 donor), `AxClass\WHSParameters_AAXWarehouseTools_Extension.xml`, `AxFormExtension\WHSParameters.AAXWarehouseTools.xml`.
**Extension class — implement exactly this (mirror of F18, cite F-numbers in comments):**

```xpp
[ExtensionOf(tableStr(WHSParameters))]
public final class WHSParameters_AAXWarehouseTools_Extension
{
    public void aaxInitPreviewAttachmentTurnsOff()        // mirror F17
    {
        this.AAXPreviewAttachmentTurnsOff = DateTimeUtil::addDays(
            DateTimeUtil::utcNow(),
            this.AAXPreviewAutoOffDays > 0 ? this.AAXPreviewAutoOffDays : 14);
    }

    private boolean aaxCanUpdateWHSParameters() { return true; }   // F19 replica

    public boolean aaxIsPreviewToWorkAttachmentEnabled()  // mirror F18 EXACTLY
    {
        if (this.AAXPreviewToWorkAttachment)
        {
            if (DateTimeUtil::utcNow() < this.AAXPreviewAttachmentTurnsOff)
            {
                return true;
            }
            boolean mustEnable = this.AAXPreviewAttachmentTurnsOff == utcDateTimeNull();
            if (this.aaxCanUpdateWHSParameters())
            {
                WHSParameters updateParameters;
                updateParameters.readPast(true);
                ttsbegin;
                select pessimisticlock firstonly updateParameters
                    where updateParameters.ParametersKey == this.ParametersKey
                       && updateParameters.AAXPreviewAttachmentTurnsOff == this.AAXPreviewAttachmentTurnsOff
                       && updateParameters.AAXPreviewToWorkAttachment == true;
                if (updateParameters)
                {
                    if (mustEnable)
                    {
                        updateParameters.aaxInitPreviewAttachmentTurnsOff();
                    }
                    else
                    {
                        updateParameters.AAXPreviewToWorkAttachment = NoYes::No;
                    }
                    updateParameters.update();
                }
                ttscommit;
            }
            return mustEnable;
        }
        return this.AAXPreviewToWorkAttachment;
    }

    public void modifiedField(FieldId _fieldId)           // CoC, mirror F20
    {
        next modifiedField(_fieldId);
        if (_fieldId == fieldNum(WHSParameters, AAXPreviewToWorkAttachment)
            && this.AAXPreviewToWorkAttachment)
        {
            this.aaxInitPreviewAttachmentTurnsOff();
        }
    }
}
```
**Accept:** DB sync OK; form shows the group on the Work creation tab; flipping toggle sets TurnsOff = now+14d.

### WP-2.2 Staging — REQ-B-6
`AxEnum\AAXWHSLabelCaptureStatus.xml` (Pending/Processing/Done/Error/Poison), `AxEdt\AAXWHSZplHash.xml` (str 64), `AxEdt\AAXWHSZplCaptured.xml` (extends `WHSZPL`), `AxTable\AAXWHSLabelCaptureStaging.xml`:
fields `FinalZpl`(AAXWHSZplCaptured), `ZplHash`, `WorkId`(WHSWorkId, relation WHSWorkTable per F8), `LicensePlateId`(WHSLicensePlateId), `LayoutId`(WHSLayoutId), `PrinterName`(WHSPrinterName), `Dpmm`(int), `WidthMm`/`HeightMm`(real), `Status`, `RetryCount`(int), `ErrorMessage`(str 1000), `ProcessedDateTime`(utcdatetime); indexes `StatusRecIdIdx(Status, RecId)`, `WorkHashIdx(WorkId, ZplHash)`; `CreatedDateTime/CreatedBy = Yes`; `CacheLookup = None`.

### WP-2.3 Capture seams — REQ-B-5 (cite F1–F5)
`AxClass\AAXWHSLabelCaptureScope.xml` — disposable static-current context (model on `WhsBatchedDocumentRoutingContext`, F5): `newFromLabel(WHSLicensePlateLabel)` factory snapshots WorkId/LP/WorkTransType + empty hash `Set`; `current()` static; `stageLabel(Name _printer, str _zpl)` computes hash (III.9), skips if in per-scope set, inserts staging row (`Status=Pending`); `dispose()` clears static.
`AxClass\WhsDocumentRouting_AAXWarehouseTools_Extension.xml`:

```xpp
[ExtensionOf(classStr(WhsDocumentRouting))]
public final class WhsDocumentRouting_AAXWarehouseTools_Extension
{
    public void printDocument(WHSWorkTransType _workTransType, WHSLicensePlateLabel _label)
    {
        // REQ-B-5: cheap lazy gate first (F18/F21 — zero overhead when off),
        // AND require a non-empty WorkId (no attachment target otherwise, F8/F1a/REQ-B-8)
        // — don't open a scope at all. Production LP labels (ProdLicensePlateLabelBuild)
        // and WorkId-less reprints are excluded here by design.
        if (!WHSParameters::find().aaxIsPreviewToWorkAttachmentEnabled() || !_label.WorkId)
        {
            next printDocument(_workTransType, _label);
            return;
        }
        using (AAXWHSLabelCaptureScope scope = AAXWHSLabelCaptureScope::newFromLabel(_label))
        {
            next printDocument(_workTransType, _label);
        }
    }

    public static void printLabelToPrinter(Name _printerName, str _label)
    {
        // F2/F5: universal ZPL chokepoint, fires BEFORE batch diversion;
        // covers legacy (F3) and new label-layout (F4 L391) paths.
        AAXWHSLabelCaptureScope scope = AAXWHSLabelCaptureScope::current();
        if (scope && _label)
        {
            scope.stageLabel(_printerName, _label);
        }
        next printLabelToPrinter(_printerName, _label);
    }
}
```
Accept the `SysObsolete` compile warning (F2). Staging insert in caller's transaction (DR-6).

### WP-2.4 Render batch — REQ-B-7 (pattern III.5)
`AAXWHSLabelCaptureRenderContract` (ChunkSize=50, MaxRetries=3), `…Service`, `…Controller` + `AxMenuItemAction\AAXWHSLabelCaptureRender.xml`. Flow per IV REQ-B-7; state transitions wrapped per F23 retry pattern.

### WP-2.5 Attachment helper — REQ-B-8
`AxClass\AAXWHSLabelAttachmentService.xml`:

```xpp
public static void ensureDocuType()   // F26: per-company, idempotent
{
    if (!DocuType::find('LBLPREV'))
    {
        DocuType docuType;
        ttsbegin;
        docuType.TypeId        = 'LBLPREV';
        docuType.Name          = "@AAXWarehouseTools:DocuTypeLabelPreview";
        docuType.TypeGroup     = DocuTypeGroup::File;
        docuType.FilePlace     = DocuFilePlace::Archive;
        docuType.ActionClassId = classNum(DocuActionArchive);  // concrete subclass — F26 corrected; attachFile() validates derives-from-DocuActionFile (F25)
        docuType.insert();
        ttscommit;
    }
}
```
Plus `attachPng(WHSWorkTable, container _png, str _fileName, AAXWHSZplHash _hash)` → stream (III.7) → `attachFileForRecord(...)` with hash in `_notes`; `hasAttachmentWithHash(WHSWorkTable, hash)` dedup query over DocuRef notes. `attachPng` MAY guard `if (!_work) return;` defensively, but the deleted-Work terminal state is the **batch's** responsibility (REQ-B-7 step 1, DR-10) — the helper never decides row status.

### WP-2.6 Security & menu — REQ-S-1/-2
`AxSecurityPrivilege\AAXWHSLabelCaptureAdminister.xml`, `AxSecurityDuty\AAXWHSLabelPreviewMaintain.xml`, `AxMenuExtension` adding render+purge items under Warehouse management → Periodic tasks.

**WP-2 accept:** compile + sync clean; V5/V6 pass (HG-2 for the device flow).

## WP-3 — Hardening

- **WP-3.1** `AAXWHSLabelCapturePurge` + action menu item — REQ-S-5, pattern III.6.
- **WP-3.2** Volume test (HG-2): device latency unchanged; batch keeps up; record numbers in `DECISIONS.md`.
- **WP-3.3** Fidelity: expose BinaryKits `DrawerOptions` font replacement as optional wrapper param; document "close, not pixel-perfect"; ZPL always beside image (already REQ-A-7).
- **WP-3.4** Seam smoke test: SysTest/runnable check asserting via reflection (`SysDictClass`) that `WhsDocumentRouting` still declares `printDocument(WHSWorkTransType, WHSLicensePlateLabel)` and static `printLabelToPrinter(Name, str)`; fail loudly otherwise (II.6 drift guard).
- **WP-3.5 (optional)** Ops list page over staging filtered Error/Poison with "Retry" (reset → Pending).
- **WP-3.6** Delete `AAXZplRenderProbe` (C-7). Final full compile + `dotnet test` + V-matrix sweep.

---

# PART VI — Verification matrix

| Gate | Verifies | How | Auto/HG |
|---|---|---|---|
| V1 | REQ-R-1/-5 | `dotnet test` | Auto |
| V2 | all X++ | xppc clean compile (Part I §4) | Auto |
| V3 | R-1 risk, REQ-R-3 | WP-0.5 probe in AOS: plain + **`^BC` barcode** ZPL renders; ER coexistence — SkiaSharp **and ZXing** loaded identities logged | HG-1 |
| V4 | REQ-A-1..8 | Both forms: live-record preview; empty-table → sample + badge; Variables layout → graceful info; **template-enabled (`EnableTemplateTranslator=Yes`) layout → graceful info, no broken render**; no-active-version info; invalid ZPL → inline error; multi-label paging (per-page temp-store URLs, incl. after a delay — no stale-URL 404); Copy/Download | HG (manual UI) |
| V5 | REQ-B-5..9 | Toggle on → mobile LP flow (receiving/put-away) → PNG(s) on correct Work; **both reprint flows** (legacy `whsWorkExecuteDisplayReprintLabel` AND Process Guide `WHSProcessGuideLabelPrintStep`) → no duplicate; empty-WorkId reprint → no attachment by design (F1a); batched printer (`IsBatchPrintEnabled`) still captured (F5) | HG-2 |
| V5a | REQ-B-7 Error/Poison + row isolation | Seed a chunk with one deliberately-failing row (invalid captured ZPL) among ≥2 valid rows; run batch: bad row → `Error`→`Poison` at `RetryCount==MaxRetries` with `ErrorMessage` set; deleted-Work row → terminal `Done` (NOT Poison, DR-10); good rows in the SAME chunk still reach `Done` with PNGs attached | HG |
| V6 | REQ-B-2/-3 | `AutoOffDays=1`, advance: capture stops (lazy) AND batch run flips toggle No + clears timestamp without form visit | HG |
| V7 | REQ-S-5 | Purge removes only aged `LBLPREV` + Done/Poison staging | HG |
| V8 | REQ-S-1 | Negative tests: no privilege → no preview, no toggle edit, no batch run | HG |
| V9 | perf | WP-3.2 | HG-2 |
| V10 | ISV R-1 final | Tier-2 deploy + probe + one real flow | HG-3 |

---

# PART VII — Decision rules & risk register

## VII.1 Decision rules (apply autonomously; log in `DECISIONS.md`)

- **DR-1 (ER coexistence: SkiaSharp AND ZXing) — LARGELY PRE-RESOLVED (F34):** Use **BinaryKits.Zpl.Viewer 1.3.1** (SkiaSharp 3.119.1 — same line as ER's 3.119.0.0). The WP-0.5 probe arbitrates exact side-by-side assembly identity with ER for **both SkiaSharp and ZXing** (ER ships strong-named legacy `zxing` 0.16.5.0; ours is modern `ZXing` ≥ 0.16.11 — different identity, likely independent binding, unproven). If (and only if) the probe shows a load conflict: SkiaSharp → try pinning exactly ER's 3.119.0; ZXing → try pinning the package version whose assembly identity coexists (or confirm independent bind by identity). If the BinaryKits API surface won't tolerate the pins, STOP → human (renderer fallbacks: zebrash sidecar / Neodynamic, design doc §3.1). NuGet calls need retry loops (flaky DNS).
- **DR-2 (strong-naming):** If signing breaks NuGet-restored unsigned dependencies or the AxReference flow, fall back to unsigned (`PublicKeyToken=null`) — proven on this box (F29, F35). Log it.
- **DR-3 (PackedQueryRun unpack):** Try `new QueryRun(container)` / `SysQueryRun` unpack of `DataSourceQuery` (F12). If it fails at compile or runtime, fall back to token-derived table names (III.8) for the seed dialog. Log it.
- **DR-4 (C# TFM):** **net472 first** (proven in-AOS precedent F35; BinaryKits 1.3.1 has a net472 build F34). `netstandard2.0` alternate. WP-0.5 probe arbitrates loadability. Log it.
- **DR-5 (donor missing):** If a named donor file doesn't exist, find the nearest same-type artifact in `AAXPOCloseHelper` → `ApplicationSuite` (in that order). Never invent schema (C-6).
- **DR-6 (staging transaction):** Default = insert in caller's transaction (rollback discards the row). If V5/V9 testing shows physically-printed labels losing their rows to outer rollbacks, switch to the `UserConnection` pattern (F21) and log it.
- **DR-7 (interop helpers):** If a `Binary`/container conversion call in III.7 doesn't exist on this version, use the direct `new System.IO.MemoryStream(bytes)` route for streams and any compiling container conversion; behavior over mechanism. Log it.
- **DR-9 (company scope):** Default = one batch job: `crossCompany` pickup over staging, group chunks by `DataAreaId`, wrap each in `changecompany` (DocuType + attach run in the row's company, F26). If testing surfaces issues, fall back to documenting one scheduled job per legal entity. Log it.
- **DR-10 (deleted attachment target):** If the snapshotted `WorkId` no longer resolves to a `WHSWorkTable` record at render time, the staging row goes terminal **`Done`** with a "work no longer exists" note — never `Error`/`Poison` (REQ-B-7 step 1). Log nothing per-row; it's expected operational behavior.
- **DR-8 (anything else material):** STOP and ask (C-10).

## VII.2 Risk register

| Risk | Status | Handling |
|---|---|---|
| R-1 SkiaSharp native asset in AOS / Tier-2+ | **Primary open risk** | WP-0.5 probe gates all feature work; DLLs in model bin; V10 Tier-2 re-proof; DR-1 fallback ladder |
| SkiaSharp collision with ElectronicReporting (3.119.0) | Open | DR-1 + probe ER-coexistence step |
| ZXing side-by-side with ER's signed `zxing` v0.16.5.0 (same bin as the SkiaSharp battleground) | **Open** | Ship ZXing.Net (≥ 0.16.11); assembly identity differs (`ZXing` vs ER's legacy `zxing`) so independent binding is likely but unproven; WP-0.5 probe renders a `^BC` barcode in-AOS + logs loaded identity; DR-1 arbitration covers ZXing too; Apache-2.0 in notices (REQ-S-4) |
| `getLayoutSource` internal | Closed (design) | REQ-A-2 replica; Variables types deferred with graceful message |
| R-5 seam drift on version upgrade | Mitigated | Exact-line verification (F1–F5); WP-3.4 reflection smoke test; `SysObsolete` successor (`printZPLLabelToPrinter`) named in II.6 |
| Handheld latency | Mitigated | Lazy boolean gate + single insert (REQ-B-5); rendering only in batch |
| R-4 PII retention | Mitigated | Auto-off both-sided guard (REQ-B-2/-7) + purge (REQ-S-5) |
| R-3 render fidelity | Accepted | WP-3.3; ZPL always beside image |
| R-6 ISV licensing | Closed | MIT/MIT/Apache-2.0, REQ-S-4 |

---

# Appendix A — Full artifact inventory (trace to WPs)

| # | Path (under `AAXWarehouseTools\`) | WP |
|---|---|---|
| 1 | `Descriptor\AAXWarehouseTools.xml` | 0.1 |
| 2 | `Projects\AAXWarehouseTools\{.sln, AAXWarehouseTools\.rnrproj}` | 0.1 |
| 3 | `AAXWarehouseTools\AxLabelFile\AAXWarehouseTools_en-US.xml` + `LabelResources\en-US\…label.txt` | 0.1 |
| 4 | `THIRD-PARTY-NOTICES.md`, `DECISIONS.md` | 0.1 |
| 5 | `Projects\AAXWarehouseTools\AtomicAx.Zpl.Render\*` + `AtomicAx.snk` | 0.2 |
| 6 | `tests\AtomicAx.Zpl.Render.Tests\*` | 0.3 |
| 7 | `AAXWarehouseTools\AxReference\AtomicAx.Zpl.Render.xml` | 0.4 |
| 8 | *(temp)* `AxClass\AAXZplRenderProbe.xml` — **deleted in WP-3.6** | 0.5 |
| 9 | `AxClass\AAXZplRenderService.xml` | 1.1 |
| 10 | `AxFormExtension\WHSLabelLayout.AAXWarehouseTools.xml` | 1.2 |
| 11 | `AxFormExtension\WHSDocumentRoutingLayout.AAXWarehouseTools.xml` | 1.2 |
| 12 | `AxClass\AAXWHSLabelPreviewController.xml` | 1.3 |
| 13 | `AxClass\AAXWHSLabelPreviewContext.xml` | 1.4 |
| 14 | `AxClass\AAXWHSLabelPreviewSampleData.xml` | 1.4 |
| 15 | `AxForm\AAXWHSLabelPreviewDialog.xml` | 1.5 |
| 16 | `AxMenuItemAction\AAXWHSLabelPreview.xml` | 1.6 |
| 17 | `AxSecurityPrivilege\AAXWHSLabelPreviewView.xml` | 1.6 |
| 18 | `AxTableExtension\WHSParameters.AAXWarehouseTools.xml` | 2.1 |
| 19 | `AxClass\WHSParameters_AAXWarehouseTools_Extension.xml` | 2.1 |
| 20 | `AxFormExtension\WHSParameters.AAXWarehouseTools.xml` | 2.1 |
| 21 | `AxEnum\AAXWHSLabelCaptureStatus.xml` | 2.2 |
| 22 | `AxEdt\AAXWHSZplHash.xml`, `AxEdt\AAXWHSZplCaptured.xml` | 2.2 |
| 23 | `AxTable\AAXWHSLabelCaptureStaging.xml` | 2.2 |
| 24 | `AxClass\AAXWHSLabelCaptureScope.xml` | 2.3 |
| 25 | `AxClass\WhsDocumentRouting_AAXWarehouseTools_Extension.xml` | 2.3 |
| 26 | `AxClass\AAXWHSLabelCaptureRender{Contract,Service,Controller}.xml` | 2.4 |
| 27 | `AxMenuItemAction\AAXWHSLabelCaptureRender.xml` | 2.4 |
| 28 | `AxClass\AAXWHSLabelAttachmentService.xml` | 2.5 |
| 29 | `AxSecurityPrivilege\AAXWHSLabelCaptureAdminister.xml` | 2.6 |
| 30 | `AxSecurityDuty\AAXWHSLabelPreviewMaintain.xml` | 2.6 |
| 31 | `AxMenuExtension\<WarehouseManagement>.AAXWarehouseTools.xml` | 2.6 |
| 32 | `AxClass\AAXWHSLabelCapturePurge.xml` + `AxMenuItemAction\AAXWHSLabelCapturePurge.xml` | 3.1 |
| 33 | Seam smoke test class | 3.4 |
| 34 | *(optional)* staging ops form + menu item | 3.5 |

# Appendix B — Label keys (seed list, extend as needed)

`GenerateLabelPreview`, `LabelPreviewDialogCaption`, `LabelNofM`, `LiveRecordBadge`, `SampleDataBadge`, `CopyZpl`, `DownloadPng`, `PreviewZplOnly`, `PreviewTemplateNotSupported`, `NoActiveVersion`, `WorkNoLongerExists`, `RenderFailed`, `ParamGroupLabelPreview`, `PreviewToWorkAttachment`, `PreviewToWorkAttachmentHelp` (mentions ZPL-only + auto-off), `PreviewAttachmentTurnsOff`, `PreviewAutoOffDays`, `PreviewDefaultDpmm`, `PreviewDefaultWidthMm`, `PreviewDefaultHeightMm`, `PreviewRetentionDays`, `DocuTypeLabelPreview`, `CaptureRenderBatch`, `CapturePurgeBatch`, `PrivPreviewView`, `PrivCaptureAdminister`, `DutyLabelPreviewMaintain`.

# Appendix E — Residual unknowns (everything else is verified; each has an owner step)

| # | Unknown | Resolves at / how |
|---|---|---|
| U-1 | Do native `libSkiaSharp.dll` + `libHarfBuzzSharp.dll` load inside this AOS process from a model `bin\`? | **WP-0.5 probe — the gate.** (Managed net472 loading is proven, F35; *native* assets are the open question, R-1) |
| U-2 | Exact side-by-side assembly identity in one process: our SkiaSharp 3.119.1 vs ER's 3.119.0.0, **and our `ZXing` ≥ 0.16.11 vs ER's strong-named legacy `zxing` 0.16.5.0** (F28) | WP-0.5 ER-coexistence step (logs both identities, renders `^BC` in-AOS); DR-1 fallback ladder |
| U-3 | Native-asset behavior on Microsoft-managed Tier-2+ | HG-3 / V10 — the only true ISV proof; no earlier resolution possible |
| U-4 | AOS CLR runtime version (never probed directly) | Mitigated, not resolved: net472 sibling DLLs load today (F35); probe confirms for ours |
| U-5 | Image control bound to a temp-store URL in a dialog form (exact control type/property) | WP-1.5: donor search (`imageLocation`, `AxFormImageControl` in ApplicationSuite); fallback = download-link-only dialog (degraded, still shippable) |
| U-6 | `PackedQueryRun` container unpack mechanics for `DataSourceQuery` | WP-1.4; DR-3 fallback ready (token-derived tables) |
| U-7 | Whether the two **reprint** flows carry a populated `WHSLicensePlateLabel.WorkId` at runtime | Production is **RESOLVED — never captured by design** (no WorkId ever, F1a). Reprint (legacy + Process Guide): WorkId comes from the originating LP record — confirm empirically in V5; REQ-B-5 skips empty-WorkId safely either way |
| U-8 | NuGet/DNS flakiness on this box | Operational only: retry loops (Part I §2); hard-offline → HG |
| U-9 | Batch-print printers (`IsBatchPrintEnabled`) end-to-end capture behavior | Logic verified static (F5 — CoC fires before diversion); confirm empirically in V5 |
| U-10 | Work deleted/archived between print-time capture and batch render | **Handled by design:** REQ-B-7 step 1 (`WHSWorkTable::find` empty-buffer check → terminal `Done`, never Poison; DR-10); exercised by V5a |

No other material unknowns: seams, coverage (F1a/F4a), translator, auto-off pattern, attachment API, DocuType validation, model/csproj/label/AxReference schemas, tool paths (xppc, SyncEngine), renderer package (versions, TFMs, deps) are all evidence-backed above.

# Appendix C — Out of scope / backlog (do NOT build in v1)

Variables/VariablesScript layout preview (blocked by F9 — v2: replicate `WhsLabelLayoutVariablesSourceGenerator` or MS extensibility request); **template-translator (`EnableTemplateTranslator=Yes`) layout preview** (`{{…}}` grammar, REQ-A-2); **production LP-label capture** (`ProdLicensePlateLabelBuild` — no Work target, F1a); Work-header "Labels generated (n)" link; ER-format labels; EPL/DPL; `ExternalLabelPrintServiceCommand` capture; wave-label path; label gallery page.

# Appendix D — References

**In-repo:** `ZPL_Label_Preview_Design_Doc.md`; `reference\links.txt`.
**X++ language (C-11):** https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/dev-ref/xpp-language-reference • https://learn.microsoft.com/en-us/training/modules/get-started-xpp-finance-operations/
**Renderer:** github.com/BinaryKits/BinaryKits.Zpl • nuget.org/packages/BinaryKits.Zpl.Viewer; fallbacks: zebrash (github.com/ingridhq/zebrash), Neodynamic ZPLPrinter Emulator SDK.
**Microsoft Learn (from `reference\links.txt`, most relevant):** WMS mobile app install/config • warehouse app framework • process guide framework • license-plate receiving (primary LP-label test flow for V5/V9) • PO receiving on mobile (secondary flow) • ER-based ZPL labels (out-of-scope alternative).
**Code evidence (this box):** `ApplicationSuite\Foundation\AxClass\WhsDocumentRouting.xml` (L199/254/276/282/349/351-357/379/391) • `WhsDocumentRoutingTranslator.xml` (L16/84/100/145/169/343/366) • `WhsBatchedDocumentRoutingContext.xml` (L84/123) • `AxTable\WHSParameters.xml` (L283/296/669/715) • `AxForm\WHSParameters.xml` (L2511-2557) • `AxClass\WHSWorkCreateHistoryPurge.xml` • `AxTable\WHSLabelLayout.xml` (L106-131) • `AxTable\WHSDocumentRoutingLayout.xml` • `ApplicationFoundation\…\AxClass\DocumentManagement.xml` • `ApplicationPlatform\…\AxClass\File.xml` • `AAXPOCloseHelper\Descriptor\…` • `AAXDataEntities\Projects\…` • `ElectronicReporting\bin\SkiaSharp.dll` (3.119.0.0).
