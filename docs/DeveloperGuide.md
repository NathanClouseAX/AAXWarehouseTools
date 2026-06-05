# AAXWarehouseTools — Developer Guide

Build, deploy, test, and troubleshooting for developers maintaining the **AAXWarehouseTools** D365 F&O ISV model and its `AtomicAx.Zpl.Render` C# rendering library.

This guide is grounded entirely in the repo: `ZPL_Label_Preview_Comprehensive_Plan.md` (the work order), `DECISIONS.md` (the engineering history), the csproj/rnrproj (deployment machinery, documented inline), `NativeLibraryPreloader.cs` + `ZplRenderService.cs`, and the test project. When something here seems surprising, the original rationale is in those files — citations use the plan's F-numbers / WP-IDs / DR-numbers and `DECISIONS.md` dates.

Paths assume the box layout from the plan (Part I §2):
- Metadata root: `C:\AOSService\PackagesLocalDirectory`
- This model / repo: `C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools`
- Model bin (the single deployed payload lives here): `C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\bin`

---

## 1. Architecture

### 1.1 The single standalone DLL — why it exists (the U-1 saga)

The renderer is **one self-contained assembly**, `AtomicAx.Zpl.Render.dll`, deployed alone into the model bin. It wraps `BinaryKits.Zpl.Viewer` (SkiaSharp-based, zero data egress — REQ-R-1/-3, C-9) to turn ZPL into PNGs in-process.

It was *not* always one DLL. The plan originally shipped a ~16-file payload (BinaryKits, SkiaSharp, SkiaSharp.HarfBuzz, HarfBuzzSharp, zxing, SixLabors.ImageSharp, BCL shims, plus `libSkiaSharp.dll` / `libHarfBuzzSharp.dll` native assets). That layout repeatedly failed to load inside the AOS. The history (`DECISIONS.md`, all dated 2026-06-04):

- **Unsigned, not strong-named (DR-2):** build-time signing succeeded with only a CS8002 warning, but the **.NET Framework runtime inside the AOS** enforces what the modern compiler dropped — a strong-named assembly cannot LOAD an unsigned dependency. Live failure: `FUSION_E_SIGNATURE_CHECK_FAILED` `0x80131044` on `BinaryKits.Zpl.Viewer, PKT=null`. (net8 tests passed because modern .NET dropped the rule — a false green.) Shipped **unsigned, `PublicKeyToken=null`** per sibling precedent F29/F35. `AtomicAx.snk` is retained in the repo but unused.
- **IIS shadow copy + load contexts:** IIS shadow-copies managed assemblies into a temp directory before loading. When SkiaSharp probed for its native library "beside itself" (`Assembly.Location`), it looked in the shadow-copy folder where the native was absent. The `NativeLibraryPreloader` was built to resolve from `Assembly.CodeBase` (the original deployment folder) instead.
- **x86 native pollution via VS ProjectReference:** the rnrproj `ProjectReference` had `Private=True`, so **every Visual Studio build re-copied the unmerged C# output and all NuGet deps** — including an **x86 `libSkiaSharp.dll`** chosen by SkiaSharp's AnyCPU targets — into the model bin. Loading an x86 native into the 64-bit AOS fails with `LoadLibrary` **Win32 error 193**. Fixed by `Private=False` + `ReferenceOutputAssembly=false` (the bin-pollution root cause noted in the WP-0.5 GATE entry).
- **Strict version binding / identity collisions:** ElectronicReporting already loads `SkiaSharp 3.119.0.0` and a strong-named legacy `zxing 0.16.5.0` (PKT `4e88037ac681fe60`) in the same process (F28). Our transitive `ZXing.Net 0.16.11` ships assembly **`zxing` with the *same* simple name AND PKT** — a version-only difference (U-2 sharpened), so the plan's "different identity → independent binding" assumption was false.

**U-1 FINAL resolution (DECISIONS.md, "single standalone DLL"):** after exhausting the load-fabric fixes above, the payload was restructured per user direction into **one assembly**:
- **ILRepack `/internalize`** merges every managed dependency into `AtomicAx.Zpl.Render.dll`. Internalizing hides all merged types, so our only public surface is our own API — merged SkiaSharp/zxing types can never clash with ElectronicReporting's copies in the same AOS process.
- **win-x64 native assets ship as embedded resources** and self-extract beside the assembly (or to `%TEMP%`) at first use.

This eliminates Fusion resolution for the whole renderer stack, all version/identity collisions with ElectronicReporting, and any multi-file deployment-correctness dependency. Validated standalone: lone DLL in an empty dir, 64-bit .NET Framework, plain + barcode render OK, zero non-framework references remain.

### 1.2 How the single DLL is built and loads

**ILRepack merge list** (csproj target `AAXILRepack`, net472 only, primary FIRST so its identity/version/attributes win):
`AtomicAx.Zpl.Render` → `BinaryKits.Zpl.Viewer` → `BinaryKits.Zpl.Label` → `SkiaSharp` → `SkiaSharp.HarfBuzz` → `HarfBuzzSharp` → `zxing` → `SixLabors.ImageSharp` → `System.Buffers` → `System.Memory` → `System.Numerics.Vectors` → `System.Runtime.CompilerServices.Unsafe` → `System.Text.Encoding.CodePages`.
`zxing.presentation` (a WPF satellite, never used by BinaryKits) is deliberately excluded. ILRepack runs with `/internalize /parallel /targetplatform:v4`.

**Embedded natives** (csproj `EmbeddedResource`, net472 only) carry logical names the preloader looks up by RID:
- `AAX.Natives.win-x64.libSkiaSharp.dll`
- `AAX.Natives.win-x64.libHarfBuzzSharp.dll`

**`NativeLibraryPreloader` extraction/load order** (`EnsureLoaded()`, idempotent under a lock; the `ZplRenderService` static ctor calls it before any SkiaSharp P/Invoke):
1. Registers an `AssemblyResolve` fallback (`ResolveFromDeployFolder`) that serves model-bin copies only when the host's strict bind fails first (e.g. a missing binding redirect) — normal/AOS bindings are never overridden.
2. `SetDllDirectory(deployDir)` (CodeBase dir, falling back to Location dir) so SkiaSharp's own path-based loader can also resolve.
3. For each native, if not already in-process (`GetModuleHandle` short-circuit — leaves any ER-loaded copy alone), tries on-disk **candidate paths** in order: `runtimes\<rid>\native\<module>` beside **CodeBase** (the model bin, survives shadow copy) → beside **Location** (shadow-copy dir under IIS) → flat layouts beside CodeBase / Location / BaseDirectory. RID is chosen by process bitness (`win-x64` for the 64-bit AOS — wrong bitness gives Win32 193).
4. If still not loaded, `ExtractAndLoadEmbedded`: reads the embedded resource and writes it to the first writable target, in order — **beside `Assembly.Location` → beside `Assembly.CodeBase` → `%TEMP%\AtomicAx.Zpl.Render\<version>\<rid>\`** — then `LoadLibrary`s it. (Post-merge, SkiaSharp's types live in our assembly, so its Location *is* ours.)
5. `StageBesideManagedAssembly` is a legacy belt for **unmerged** layouts (the net8 test build, or a dev deployment of separate DLLs) — copies the native next to the managed SkiaSharp/HarfBuzzSharp assembly. No-op when merged or already in-process.

The full probe log is captured in `NativeLibraryPreloader.Diagnostics` and surfaced on failures (see §4).

### 1.3 The X++ ↔ C# surface

X++ calls the C# library only through `AAXZplRenderService` (`AxClass\AAXZplRenderService.xml`), which wraps `AtomicAx.Zpl.Render.ZplRenderService`:

| C# (`ZplRenderService`) | Purpose | X++ access |
|---|---|---|
| `RenderToPngs(zpl, dpmm, widthMm, heightMm) → ZplRenderResult` | **The API X++ calls.** One PNG per `^XA…^XZ` block. | `AAXZplRenderService::renderToPngList` |
| `RenderToPngList(...) → IList<byte[]>` | Same semantics; retained for C# tests. | (not called from X++) |
| `RotatePng(byte[], quarterTurnsClockwise) → byte[]` | Rotate a PNG clockwise; `turns==0` returns the same reference unchanged. | `AAXZplRenderService::rotatePng` |
| `ComputeHash(zpl) → string` | SHA-256 lowercase hex (64 chars) of the UTF-8 ZPL (III.9 — shared by X++ and tests). | — |
| `GetTokenRecordNames(zpl) → string[]` | Read-only `$Record.Field$` discovery (III.8/C-5; substitution stays the translator's job). | — |
| `GetRuntimeDiagnostics() → string` | Preloader probe log + loaded renderer assembly identities (U-1/U-2 support diagnostics). | — |

**`ZplRenderResult` rationale (DECISIONS III.7):** X++ cannot declare a closed generic CLR type like `IList<byte[]>`, so the X++-friendly wrapper exposes `Count` / `GetPng(i)` instead. `AAXZplRenderService.renderOnce` walks it and converts each `byte[]` to an X++ `container` via the `Binary`/`MemoryStream` bridge.

**Dimension policy (REQ-R-1, DECISIONS REQ-R-1):** `dpmm <= 0` always throws `ZplDimensionsMissingException` (density must come from the caller — X++ owns the fallback to the `WHSParameters` defaults in `renderToPngList`); `^PW`/`^LL` parsing only supplies width/height. Empty/whitespace ZPL or any analyzer/drawer failure → `ZplRenderException` (original message preserved). On native-load-shaped failures the exception message gets the preloader log + Skia load-failure report appended for diagnosability.

---

## 2. Build & deploy

### 2.1 C# library

Build from the C# project folder (`Projects\AAXWarehouseTools\`). A normal build merges and deploys the standalone DLL.

```powershell
# REQUIRED before a deploying build: stop IIS or the build fails on file locks
# (the model bin DLL is loaded by the running AOS — see AAX dev-box operations).
iisreset /stop

dotnet build "C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\Projects\AAXWarehouseTools\AtomicAx.Zpl.Render\AtomicAx.Zpl.Render.csproj"

iisreset /start
```

What a deploying `dotnet build` does on the **net472** leg (csproj targets, `AAXSkipDeploy != true`):
1. `AAXILRepack` — merges the managed payload into `…\bin\…\merged\AtomicAx.Zpl.Render.dll` (`/internalize`).
2. `AAXCopyToModelBin` — **scrubs the stale multi-DLL payload** from the model bin (deletes the 16 named DLLs + the `runtimes` folder), then copies the single merged `AtomicAx.Zpl.Render.dll` into `C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\bin\`.

> The net8.0 leg ships nothing and does not affect the deployed artifact — it exists only so the xunit test project can `ProjectReference` the same source.

**Test-only / non-deploying build** (skips ILRepack + model-bin copy + scrub — does not touch the running AOS, no `iisreset` needed):

```powershell
dotnet build "...\AtomicAx.Zpl.Render.csproj" -p:AAXSkipDeploy=true
```

**Flaky NuGet DNS** (Part I §2 / U-8): restore can fail on intermittent DNS. Re-run `dotnet restore` / `dotnet build`; wrap in a retry loop if scripting.

### 2.2 X++ compile

Exact CLI from the plan (Part I §4) — verify `xppc.exe` exists at that path first; a clean compile = zero errors in `BuildModelResult.xml`:

```powershell
C:\AOSService\PackagesLocalDirectory\bin\xppc.exe `
  -metadata="C:\AOSService\PackagesLocalDirectory" `
  -compilermetadata="C:\AOSService\PackagesLocalDirectory" `
  -modelmodule=AAXWarehouseTools `
  -output="C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\bin" `
  -referencefolder="C:\AOSService\PackagesLocalDirectory" `
  -log=BuildModelResult.log -xmllog=BuildModelResult.xml
```

**Label compilation** (`labelc.exe`, as recorded in `CompileLabels.xml`) — needed when label keys change:

```powershell
C:\AOSService\PackagesLocalDirectory\Bin\labelc.exe `
  -metadata="C:\AOSService\PackagesLocalDirectory" `
  -output="C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\Resources" `
  -modelmodule="AAXWarehouseTools"
```

> Compiled label resources under `Resources\` are build output and are git-ignored — do not commit them.

### 2.3 DB sync

Needed once tables/table-extensions change (this model adds `AAXWHSLabelCaptureStaging` + a `WHSParameters` extension). Sync is **human-gated** (see AAX dev-box operations — DB sync is not automated here). Options (plan Part I §4):

- `SyncEngine.exe` at `C:\AOSService\PackagesLocalDirectory\bin\SyncEngine.exe`:
  ```powershell
  C:\AOSService\PackagesLocalDirectory\bin\SyncEngine.exe -syncmode=fullall `
    -metadatabinaries="C:\AOSService\PackagesLocalDirectory" `
    -connect="<DataAccess connection string from C:\AOSService\webroot\web.config>"
  ```
- Or run sync from Visual Studio (the rnrproj has `DBSyncInBuild=True`). If the connection string isn't discoverable → **HUMAN GATE** (HG-4), the human runs sync.

### 2.4 iisreset after everything — and WHY

Run `iisreset` (or restart the AOS service) **after copying DLLs to the model bin and after first model creation** (Part I §4). On this box the rule is stronger (AAX dev-box operations):
- **Before** a C# redeploy: `iisreset /stop` — the AOS holds the model-bin DLL open; otherwise the build's copy/scrub fails on file locks.
- **After every native fix:** `iisreset` — a `TypeInitializationException` from a previously-failed SkiaSharp init is **cached for the life of the process**. Without recycling, you keep seeing the *old* failure even after deploying the fix (a false red, the mirror of the bitness false-green).

> Note the bitness false-green lesson: net8 tests can pass while the net472 in-AOS load fails (the strong-name and native-bitness rules only bite the Framework runtime). "Green tests" is necessary but not sufficient — the WP-0.5 in-AOS probe is the real gate.

### 2.5 CRITICAL warnings

> **(a) NEVER set the rnrproj `ProjectReference` back to `Private=True`.**
> Quoting the inline comment in `…\AAXWarehouseTools.rnrproj`:
> *"Private=False is CRITICAL: with Private=True every VS build copied the UNMERGED C# output + all NuGet deps (including an x86 libSkiaSharp.dll picked by SkiaSharp's AnyCPU targets) into the model bin, re-polluting the single-DLL payload and breaking the renderer in the 64-bit AOS (LoadLibrary error 193). Deployment is owned exclusively by the csproj's AAXCopyToModelBin target."*
> Keep `Private=False` + `ReferenceOutputAssembly=false`. Deployment belongs to the csproj, not the reference.

> **(b) The model bin must contain exactly ONE payload DLL.**
> If renders break, check `C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\bin\` for stale payload files — `BinaryKits.*.dll`, `SkiaSharp*.dll`, `HarfBuzzSharp.dll`, `zxing*.dll`, `SixLabors.ImageSharp.dll`, the `System.*` shims, loose `libSkiaSharp.dll` / `libHarfBuzzSharp.dll`, or a `runtimes\` folder. Any of these can shadow the merged DLL. `iisreset /stop`, delete them, rebuild (the `AAXCopyToModelBin` scrub does this automatically on a deploying build).

> **(c) A corrupted metadata XML *anywhere* in `PackagesLocalDirectory` fails every compile.**
> The X++ compiler is global. A `MetadataCorruptedException` **names the offending file** — read the message, fix/restore that specific XML (even if it's in another model), then recompile.

---

## 3. Testing

### 3.1 C# unit tests (`dotnet test`)

```powershell
dotnet test "C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\tests\AtomicAx.Zpl.Render.Tests\AtomicAx.Zpl.Render.Tests.csproj"
```

The test project (`AtomicAx.Zpl.Render.Tests.csproj`) **multi-targets `net472` + `net8.0`**, each leg referencing the matching library TFM:
- **net8.0 leg** — the runtime resolves the win-x64 natives itself; the preloader's embedded-extraction path stays dormant. General correctness coverage.
- **net472 leg** — this is the important one: it **exercises the `NativeLibraryPreloader`'s embedded-resource extraction** (`AAX.Natives.win-x64.*`, net472-only resources) under the same runtime the AOS uses, and `RuntimeIdentifier=win-x64` forces the natives beside the test assembly — a shadow-copying-host-like check that the natives load.

Coverage (`ZplRenderServiceTests.cs`, REQ-R-5 plus rotation contract): valid 4×2 render = exactly one non-empty PNG; multi-label = two PNGs; missing dims and `dpmm=0` → `ZplDimensionsMissingException`; explicit dims override (no `^PW`/`^LL`); garbage/null → `ZplRenderException` (never a raw `NullReferenceException`); `^BC` Code-128 barcode renders (the ZXing path through BinaryKits); `ComputeHash` is 64-char lowercase hex, stable, differs for different input, and matches the known empty-string SHA-256 vector; `GetTokenRecordNames` distinct + record-less skip; and the full `RotatePng` contract (dimension swap on odd turns, identity on 0, `-1 == 3`, null/empty throws).

> **The merged-artifact invariant:** the net472 test leg only proves the merge if it runs against the merged/extracted layout. PNG magic-header assertions (`AssertPng`) catch a broken native load (no PNG bytes ⇒ Skia never initialized). A green net472 leg is the closest local proxy for "the single DLL self-extracts and renders" before the in-AOS probe.

### 3.2 X++ — no unit tests

There are **no automated X++ unit tests**. X++ verification is the **manual V-matrix** in the plan **Part VI** (V2 xppc clean compile; V3 in-AOS render probe = HG-1; V4 Feature A preview UI; V5/V5a capture + render batch row isolation = HG-2; V6 auto-off; V7 purge; V8 security; V9 perf; **V10 Tier-2 deploy + probe + one real flow = HG-3**). Most rows are human gates by nature (device flows, UI, sandbox deploy).

---

## 4. Troubleshooting runbook

Symptom → cause → fix, from the real U-1 history (`DECISIONS.md`, plan Part VII / Appendix E):

| Symptom | Cause | Fix |
|---|---|---|
| `FUSION_E_SIGNATURE_CHECK_FAILED 0x80131044`, "strongly-named assembly required" | The DLL was strong-named; the .NET Framework AOS runtime refuses a signed assembly loading unsigned deps (DR-2). | Ship **unsigned** (`PublicKeyToken=null`). Already the case — `SignAssembly=false` in the csproj. Do not re-enable signing. |
| `Unable to load … libSkiaSharp` / `libHarfBuzzSharp`; `DllNotFoundException`; `TypeInitializationException` from SkiaSharp | Native not found/loaded beside the (shadow-copied) assembly (U-1). | The exception message has the diagnostics appended — **read the preloader log + Skia load-failure report** (below). Then: confirm one merged DLL in the bin (warning b), `iisreset` to clear cached TypeInit (§2.4), rebuild. |
| `LoadLibrary` **Win32 error 193** | An **x86** native got loaded into the 64-bit AOS — almost always bin pollution from a `Private=True` VS build (warning a). | Restore `Private=False`; scrub the bin of stray `libSkiaSharp.dll` / `runtimes\` (warning b); `iisreset /stop`, rebuild, `iisreset /start`. |
| "Instances of abstract classes cannot be created" when creating DocuType `LBLPREV` | `DocuType.insert()` instantiates the action class; `DocuActionFile` is **abstract** (F26 corrected live). | Set `ActionClassId = classNum(DocuActionArchive)` — the concrete `final` subclass that still satisfies `attachFile()`'s `isEqualOrSuperclass(_, DocuActionFile)` check (MS precedent `DocuUpload`). Already fixed in `AAXWHSLabelAttachmentService::ensureDocuType()`. |
| `Docu_IncorrectDocuType` on attach | DocuType missing the correct `ActionClassId` (F25). | Same as above — `ensureDocuType()` must run (idempotent, per-company). |
| Deploying `dotnet build` fails copying to the model bin (file in use / access denied) | The running AOS holds the DLL open. | `iisreset /stop` before the build, `iisreset /start` after (§2.4). |
| Fix deployed but the *same* old error keeps appearing | A `TypeInitializationException` is cached for the process lifetime (§2.4). | `iisreset` after the native/DLL change. The error may be stale. |
| Native extraction logs "Access … denied" / "extraction to … failed" | Expected when the model bin is read-only (Tier-2). The preloader falls through to the next target. | Confirm the log then shows `loaded extracted native from …\AppData\…\Temp\AtomicAx.Zpl.Render\<version>\win-x64\…` — the `%TEMP%` fallback is the designed safety net (§6). |
| `error.txt` shows an old failure | `error.txt` is the convention for the user pasting the latest AOS error back to the agent; timestamps can lag a fix. | Cross-check against the current build/`iisreset` time; an instrumented build's log only reflects the run *after* the recycle. |

### How to read the preloader log & Skia load-failure report

On any native-shaped failure, `ZplRenderService` appends two blocks to the thrown message: **"Native preloader log:"** (from `NativeLibraryPreloader.Diagnostics`) and **"== Skia load-failure report =="** (from `BuildSkiaLoadFailureReport`). In X++ the `AAXZplRenderService` surfaces the **full** outer→inner chain (`innermostMessage`) into `@AAXWarehouseTools:RenderFailed`, so the whole report reaches the preview error dialog. Reading the probe lines:

Preloader log lines:
- `AssemblyResolve fallback registered …` — the strict-bind safety net is armed.
- `Process bitness: x64` — must be x64 in the AOS; `x86` would explain Win32 193.
- `BaseDirectory / Assembly.Location dir / Assembly.CodeBase dir` — where it looked; under IIS, Location is the shadow-copy temp dir and CodeBase is the real model bin.
- `SetDllDirectory(<dir>): OK|FAILED Win32 N` — process DLL search path augmented.
- `<module>: already loaded in-process (left as-is)` — another model (e.g. ER) already loaded it; we don't fight the bind.
- `<module>: not found at <path>` / `loaded from <path>` / `LoadLibrary failed (Win32 error N) for <path>` — per-candidate on-disk probe results.
- `<module>: extracted embedded native to <path>` / `loaded extracted native from <path>` / `LoadLibrary failed … for extracted <path>` — the embedded self-extract path (this is the normal single-DLL route).
- `<module> in-process after preload: True|False` — the bottom line. `True` for both modules = natives are loaded.

Skia load-failure report lines:
- `Throwing site` / `EXECUTING assembly` — which assembly actually P/Invoked (load contexts can duplicate identity-equal assemblies).
- `Compile-time-bound SkiaSharp` and `Loaded SkiaSharp #n` / `SkiaSharp instances in AppDomain: n` — if `n > 1`, two SkiaSharp copies coexist (the original U-2 collision). After the merge this should report our single merged assembly as the renderer (the WP-0.5 identity log showed *only* `AtomicAx.Zpl.Render`).
- `exists <root>\x64\libSkiaSharp.dll: True|False` / `exists <root>\libSkiaSharp.dll: …` — on-disk existence at SkiaSharp 3.119's own candidate paths.

---

## 5. Extending

- **Adding a new metadata artifact:** follow the plan's **golden rule (III.1)** — read the named donor for that artifact type, copy its full XML structure, edit values only. The F&O metadata loader is schema-strict and **element order matters** (C-6). The donor table in III.1 maps every artifact type (AxClass, AxTable, AxForm, AxEnum, AxEdt, security, label file, AxReference, …) to a concrete donor file, mostly under `AAXIntegrationOperations` (the primary donor).
- **C# donor methodology:** plan **Part III** — the renderer mirrors the in-AOS-proven `AAXIntegrationOperations` C# precedent (net472, old-style/SDK-style, unsigned, model-bin load — F35).
- **Label-file conventions (C-4 / F36 / III.10):** every user-facing string is a label, referenced `@AAXWarehouseTools:<Key>` (prefix = `LabelFileId` = `AAXWarehouseTools`). Content file is `AxLabelFile\LabelResources\en-US\AAXWarehouseTools.en-US.label.txt` — **dot before `en-US`, not underscore** — `Key=Text` with optional ` ;comment` continuation lines. No hard-coded strings in X++. Seed key list: Appendix B.
- **Day-to-day C-constraints that bite:**
  - **C-2:** never call X++ `internal` methods from this model — they're inaccessible cross-model and the compiler rejects them (e.g. `WHSLabelLayout.getLayoutSource()` is `internal` → ZPL resolution is replicated, F9).
  - **C-3:** every CoC extension is a `final` class `<Target>_AAXWarehouseTools_Extension` with `[ExtensionOf(...)]`, and **`next` must be called unconditionally exactly once** (compiler-enforced — DECISIONS C-3: the original gate-then-early-return skeleton was illegal; carry an `active` flag instead). Never wrap `[Hookable(false)]` methods.
  - **C-5:** token *substitution* must reuse `WhsDocumentRoutingTranslator`; only read-only regex *discovery* is allowed.
  - No internal Microsoft APIs; copy donor schema, don't invent (C-6).
- **Deferred backlog — do NOT build in v1 (Appendix C):** Variables/VariablesScript layout preview (blocked by F9); template-translator (`EnableTemplateTranslator=Yes`, `{{…}}` grammar) layout preview; production LP-label capture (`ProdLicensePlateLabelBuild` — no Work target, F1a); Work-header "Labels generated (n)" link; ER-format labels; EPL/DPL; `ExternalLabelPrintServiceCommand` capture; wave-label path; label gallery page. Also deferred: seed dialog (REQ-A-3 → v2); Copy-ZPL clipboard button (no provable MS clipboard donor — read-only selectable text is the affordance, DECISIONS REQ-A-7).

---

## 6. Tier-2 / packaging notes

- **Payload = ONE bin file.** The deployable ISV package carries a single `AtomicAx.Zpl.Render.dll` in the model bin (plus the X++ metadata). This is the entire point of the U-1 single-DLL restructure — Tier-2 packaging trivially carries one file; there is no multi-file deployment-correctness dependency.
- **Natives self-extract; the model bin may be read-only in Tier-2.** The win-x64 natives are embedded in the DLL. The preloader extracts beside `Location` → beside `CodeBase` → **`%TEMP%\AtomicAx.Zpl.Render\<version>\win-x64\`**. On a Microsoft-managed Tier-2 environment where the model bin is **read-only**, the first two writes fail (logged, expected) and the `%TEMP%` path is used — this is by design (DECISIONS U-1 FINAL). Access-denied lines in the log are not an error there.
- **What V10 must verify (plan V10 / HG-3 / U-3):** deploy the deployable package to a Tier-2 sandbox and **re-run the WP-0.5-style render probe** in that environment — plain ZPL renders, a `^BC` barcode renders (ZXing path in-AOS), and one real LP-print flow attaches PNG(s). Native-asset behavior on Microsoft-managed Tier-2+ is the **only true ISV proof** (U-3) and cannot be resolved earlier; capture the `GetRuntimeDiagnostics()` / preloader log and record the result in `DECISIONS.md`.
