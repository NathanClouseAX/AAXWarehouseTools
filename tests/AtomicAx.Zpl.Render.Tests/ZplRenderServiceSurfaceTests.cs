using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AtomicAx.Zpl.Render;
using Xunit;

namespace AtomicAx.Zpl.Render.Tests
{
    /// <summary>
    /// Coverage for the full current public surface added on top of the original 20 tests:
    /// GetRuntimeDiagnostics, NativeLibraryPreloader extraction semantics (net472 leg), token-scan
    /// hardening checked against the actual regex, a non-empty ComputeHash known vector computed
    /// independently in the test, render dimension edge cases, and the ILRepack merged-artifact
    /// invariant. Deterministic, no network, no writes outside temp/test dirs.
    /// </summary>
    public class ZplRenderServiceSurfaceTests
    {
        // 4x2 inch label at 8 dpmm => ^PW812 (101.6mm) ^LL406 (50.8mm).
        private const string Valid4x2 =
            "^XA^PW812^LL406^FO50,50^A0N,40,40^FDHello^FS^XZ";

        private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47 };

        private static void AssertPng(byte[] bytes)
        {
            Assert.NotNull(bytes);
            Assert.True(bytes.Length >= 4, "PNG byte array too short for a magic header.");
            for (int i = 0; i < PngMagic.Length; i++)
            {
                Assert.Equal(PngMagic[i], bytes[i]);
            }
        }

        // ------------------------------------------------------------------
        // 2. GetRuntimeDiagnostics
        // ------------------------------------------------------------------

        [Fact]
        public void GetRuntimeDiagnostics_NonEmpty_ContainsPreloaderHeaderAndRendererIdentity()
        {
            string diag = ZplRenderService.GetRuntimeDiagnostics();

            Assert.False(string.IsNullOrWhiteSpace(diag), "Diagnostics must be non-empty.");
            // Preloader section header emitted by GetRuntimeDiagnostics.
            Assert.Contains("== Native preloader ==", diag);
            // The renderer assembly identity line.
            Assert.Contains("AtomicAx.Zpl.Render:", diag);
            Assert.Contains("AtomicAx.Zpl.Render,", diag); // full name token (assembly identity)
        }

        [Fact]
        public void GetRuntimeDiagnostics_AfterRender_OnNet472_ShowsNativesLoaded()
        {
            // Force the native-load path by actually rendering first.
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(Valid4x2, 8);
            Assert.Single(pngs);

            string diag = ZplRenderService.GetRuntimeDiagnostics();
            Assert.False(string.IsNullOrWhiteSpace(diag));

#if NET472
            // On .NET Framework (the AOS runtime) the preloader is the load mechanism, so its log
            // must mention libSkiaSharp. Assert loosely (substring), not a brittle full string.
            Assert.Contains("libSkiaSharp", diag, StringComparison.OrdinalIgnoreCase);
#endif
        }

        // ------------------------------------------------------------------
        // 3. NativeLibraryPreloader extraction semantics (net472 leg only).
        //    On net8.0 the runtime resolves natives itself; the embedded extraction
        //    path is dormant, so these assertions only apply under NET472.
        // ------------------------------------------------------------------

#if NET472
        [Fact]
        public void Preloader_AfterRender_NativeExistsBesideAssemblyOrUnderTemp()
        {
            // First render triggers EnsureLoaded -> native must be resolvable on disk somewhere
            // the preloader staged/extracted it.
            IList<byte[]> first = ZplRenderService.RenderToPngList(Valid4x2, 8);
            Assert.Single(first);

            Assembly self = typeof(ZplRenderService).Assembly;

            // The VSTest .NET Framework host shadow-copies the test assembly, so Assembly.Location
            // points at the shadow-copy cache while Assembly.CodeBase still points at the real build
            // output (exactly the IIS shadow-copy scenario the preloader handles). Accept either the real
            // output dir (CodeBase), the Location dir, or the %TEMP% extraction dir.
            string locationDir = null;
            try { locationDir = Path.GetDirectoryName(self.Location); } catch { /* byte-loaded */ }

            string codeBaseDir = null;
            try { codeBaseDir = Path.GetDirectoryName(new Uri(self.CodeBase).LocalPath); }
            catch { /* may be unavailable */ }

            string version;
            try { version = self.GetName().Version.ToString(); } catch { version = "0"; }
            string tempDir = Path.Combine(
                Path.GetTempPath(),
                Path.Combine("AtomicAx.Zpl.Render", Path.Combine(version, "win-x64")));

            bool Has(string dir) =>
                !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "libSkiaSharp.dll"));

            Assert.True(
                Has(locationDir) || Has(codeBaseDir) || Has(tempDir),
                "libSkiaSharp.dll must exist beside the assembly Location (" + (locationDir ?? "<null>") +
                "), CodeBase (" + (codeBaseDir ?? "<null>") +
                "), or under " + tempDir + " after the first render.");
        }

        [Fact]
        public void Preloader_SecondRender_IsIdempotentAndSucceeds()
        {
            // EnsureLoaded is guarded; a second render call must succeed exactly like the first.
            IList<byte[]> first = ZplRenderService.RenderToPngList(Valid4x2, 8);
            IList<byte[]> second = ZplRenderService.RenderToPngList(Valid4x2, 8);

            Assert.Single(first);
            Assert.Single(second);
            AssertPng(first[0]);
            AssertPng(second[0]);
        }
#endif

        // ------------------------------------------------------------------
        // 4. Token scan hardening — expectations checked against the actual regex:
        //    \$(?:(?<record>\w+?)\.)?(?<field>\w+?)(?<methodIndicator>\(\))?
        //      (?:\[(?<lineIndex>[0-9]{1,3})\])?(?::(?<format>.*?))?\$
        // ------------------------------------------------------------------

        [Fact]
        public void GetTokenRecordNames_MethodIndicatorToken_StillYieldsRecord()
        {
            // $Rec.method()$ -> record 'Rec', field 'method', methodIndicator '()'.
            string[] records = ZplRenderService.GetTokenRecordNames("$Order.Total()$");
            Assert.Equal(new[] { "Order" }, records);
        }

        [Fact]
        public void GetTokenRecordNames_LineIndexVariant_YieldsRecord()
        {
            // $Rec.field[2]$ -> record 'Rec' (lineIndex captured separately, not part of record).
            string[] records = ZplRenderService.GetTokenRecordNames("$PurchLine.ItemId[2]$");
            Assert.Equal(new[] { "PurchLine" }, records);
        }

        [Fact]
        public void GetTokenRecordNames_FormatVariant_YieldsRecord()
        {
            // $Rec.field:format$ -> record 'Rec' (':..10' is the format capture).
            string[] records = ZplRenderService.GetTokenRecordNames("$PurchLine.ItemId:..10$");
            Assert.Equal(new[] { "PurchLine" }, records);
        }

        [Fact]
        public void GetTokenRecordNames_AllVariantsCombined_YieldsRecord()
        {
            // $Rec.field()[2]:fmt$ -> record 'Rec'.
            string[] records = ZplRenderService.GetTokenRecordNames("$Line.Item()[2]:fmt$");
            Assert.Equal(new[] { "Line" }, records);
        }

        [Fact]
        public void GetTokenRecordNames_DoubleDollarLiteral_ProducesNoRecord()
        {
            // '$$' is a literal passthrough: the field group needs >=1 char, so $$ matches no token.
            // Mixed with a real token, only the real record is returned and the '$$5' yields nothing.
            string[] none = ZplRenderService.GetTokenRecordNames("literal $$ passthrough");
            Assert.Empty(none);

            string[] mixed = ZplRenderService.GetTokenRecordNames("$Acct.Bal$ price is $$5");
            Assert.Equal(new[] { "Acct" }, mixed);
        }

        [Fact]
        public void GetTokenRecordNames_PreservesCase_AndTreatsDifferentCaseAsDistinct()
        {
            // HashSet is StringComparer.Ordinal -> case is preserved AND case-sensitive distinct.
            string[] records = ZplRenderService.GetTokenRecordNames("$REC.Field$ $rec.field$");

            Assert.Equal(2, records.Length);
            Assert.Contains("REC", records);
            Assert.Contains("rec", records);
        }

        // ------------------------------------------------------------------
        // 5. ComputeHash known vector (non-empty), computed independently in the test.
        // ------------------------------------------------------------------

        [Fact]
        public void ComputeHash_NonEmptyKnownVector_MatchesIndependentSha256()
        {
            const string input = "AtomicAx-ZPL";

            string expected;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                expected = sb.ToString();
            }

            Assert.Equal(expected, ZplRenderService.ComputeHash(input));
        }

        // ------------------------------------------------------------------
        // 6. Render dimension edge cases.
        // ------------------------------------------------------------------

        [Fact]
        public void Render_PwPresentButLlAbsent_ThrowsDimensionsMissing()
        {
            const string pwOnly = "^XA^PW812^FO50,50^A0N,40,40^FDHi^FS^XZ";
            Assert.Throws<ZplDimensionsMissingException>(
                () => ZplRenderService.RenderToPngList(pwOnly, 8));
        }

        [Fact]
        public void Render_LlPresentButPwAbsent_ThrowsDimensionsMissing()
        {
            const string llOnly = "^XA^LL406^FO50,50^A0N,40,40^FDHi^FS^XZ";
            Assert.Throws<ZplDimensionsMissingException>(
                () => ZplRenderService.RenderToPngList(llOnly, 8));
        }

        [Fact]
        public void Render_ExplicitDims_WinOverGarbagePwInZpl()
        {
            // ^PW value is garbage/zero but explicit dims are supplied -> explicit wins, renders.
            // (widthMm>0 && heightMm>0 short-circuits the ^PW/^LL parse entirely.)
            const string garbagePw = "^XA^PW0^FO50,50^A0N,40,40^FDHi^FS^XZ";
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(garbagePw, 8, 101.6, 50.8);

            Assert.Single(pngs);
            AssertPng(pngs[0]);
        }

        // ------------------------------------------------------------------
        // 7. Merged-artifact invariant (conditional on the deployed merged DLL existing).
        //    Reads bytes (does NOT lock the file) and asserts the ILRepack /internalize guarantee:
        //    no references to SkiaSharp / BinaryKits / zxing / SixLabors remain.
        // ------------------------------------------------------------------

        [Fact]
        public void MergedArtifact_HasNoUnmergedThirdPartyReferences()
        {
            const string mergedPath =
                @"C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\bin\AtomicAx.Zpl.Render.dll";

            if (!File.Exists(mergedPath))
            {
                // The deployed merged artifact is not present (e.g. CI elsewhere) -> nothing to check.
                return;
            }

            // Read bytes so we never lock the on-disk file.
            byte[] raw = File.ReadAllBytes(mergedPath);
            Assembly merged = Assembly.Load(raw);

            string[] referenced = merged
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToArray();

            string[] forbidden = { "SkiaSharp", "BinaryKits", "zxing", "SixLabors" };
            foreach (string name in referenced)
            {
                foreach (string bad in forbidden)
                {
                    Assert.False(
                        name.IndexOf(bad, StringComparison.OrdinalIgnoreCase) >= 0,
                        "Merged AtomicAx.Zpl.Render.dll must not reference '" + name +
                        "' (ILRepack /internalize should have merged it).");
                }
            }
        }

        // A leading ^XA…^XZ printer-configuration preamble (darkness/print-rate/media setup,
        // no drawable content) must NOT yield a spurious blank label — only the real label.
        private const string ConfigPreamblePlusLabel =
            "CT~~CD,~CC^~CT~" +
            "^XA~TA000~JSN^LT0^MNW^MTT^PON^PMN^LH0,0^JMA^PR8,8~SD15^JUS^LRN^CI0^XZ" +
            "^XA^MMT^PW812^LL0609^LS0^BY3,3,262^FT658,186^BAI,,Y,N^FDABC123451^FS" +
            "^FT660,457^A0I,39,38^FH\\^FDContainer ID2^FS" +
            "^FT660,515^A0I,39,38^FH\\^FDShipment: SHIP0013^FS^PQ1,0,1,Y^XZ";

        [Fact]
        public void RenderToPngList_SkipsConfigOnlyPreambleBlock_RendersOneLabel()
        {
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(ConfigPreamblePlusLabel, 8);

            Assert.Single(pngs);
            Assert.True(pngs[0] != null && pngs[0].Length > 0);
        }

        [Fact]
        public void RenderToPngList_AllConfigOnly_FallsBackToRenderingTheBlocks()
        {
            // Degenerate: only a config block, nothing printable -> fallback renders it (count >= 1),
            // never returns an empty list.
            const string configOnly = "^XA~SD15^PR8,8^MNW^MTT^XZ";
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(configOnly, 8, 101.6, 50.8);

            Assert.True(pngs.Count >= 1);
        }
    }
}
