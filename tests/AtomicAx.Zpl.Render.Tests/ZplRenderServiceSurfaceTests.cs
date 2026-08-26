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
    /// Tests the diagnostics, native preloader, token-scan, hashing, dimension edge cases, and
    /// merged-artifact behavior of <see cref="ZplRenderService"/>. The tests are deterministic and
    /// write only to temporary and test directories.
    /// </summary>
    public class ZplRenderServiceSurfaceTests
    {
        // 4x2 inch label at 8 dpmm: ^PW812 (101.6 mm) ^LL406 (50.8 mm)
        private const string Valid4x2 =
            "^XA^PW812^LL406^FO50,50^A0N,40,40^FDHello^FS^XZ";

        private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47 };

        /// <summary>
        /// Asserts that the bytes are a PNG image by checking the magic header.
        /// </summary>
        /// <param name="bytes">The bytes to check.</param>
        private static void AssertPng(byte[] bytes)
        {
            Assert.NotNull(bytes);
            Assert.True(bytes.Length >= 4, "PNG byte array too short for a magic header.");
            for (int i = 0; i < PngMagic.Length; i++)
            {
                Assert.Equal(PngMagic[i], bytes[i]);
            }
        }

        // GetRuntimeDiagnostics

        [Fact]
        public void GetRuntimeDiagnostics_NonEmpty_ContainsPreloaderHeaderAndRendererIdentity()
        {
            string diag = ZplRenderService.GetRuntimeDiagnostics();

            Assert.False(string.IsNullOrWhiteSpace(diag), "Diagnostics must be non-empty.");
            // Preloader section header
            Assert.Contains("== Native preloader ==", diag);
            // Renderer assembly identity
            Assert.Contains("AtomicAx.Zpl.Render:", diag);
            Assert.Contains("AtomicAx.Zpl.Render,", diag); // full name token (assembly identity)
        }

        [Fact]
        public void GetRuntimeDiagnostics_AfterRender_OnNet472_ShowsNativesLoaded()
        {
            // Render first to force the native-load path
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(Valid4x2, 8);
            Assert.Single(pngs);

            string diag = ZplRenderService.GetRuntimeDiagnostics();
            Assert.False(string.IsNullOrWhiteSpace(diag));

#if NET472
            // On .NET Framework the preloader is the load mechanism, so its log must mention libSkiaSharp
            Assert.Contains("libSkiaSharp", diag, StringComparison.OrdinalIgnoreCase);
#endif
        }

        // NativeLibraryPreloader extraction, which only applies under NET472

#if NET472
        [Fact]
        public void Preloader_AfterRender_NativeExistsBesideAssemblyOrUnderTemp()
        {
            // The first render must leave the native resolvable on disk
            IList<byte[]> first = ZplRenderService.RenderToPngList(Valid4x2, 8);
            Assert.Single(first);

            Assembly self = typeof(ZplRenderService).Assembly;

            // The test host shadow-copies the test assembly, so accept the Location, CodeBase, or temp folder
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
            // A second render must succeed like the first
            IList<byte[]> first = ZplRenderService.RenderToPngList(Valid4x2, 8);
            IList<byte[]> second = ZplRenderService.RenderToPngList(Valid4x2, 8);

            Assert.Single(first);
            Assert.Single(second);
            AssertPng(first[0]);
            AssertPng(second[0]);
        }
#endif

        // Token scan variants

        [Fact]
        public void GetTokenRecordNames_MethodIndicatorToken_StillYieldsRecord()
        {
            // A method-indicator token still yields its record
            string[] records = ZplRenderService.GetTokenRecordNames("$Order.Total()$");
            Assert.Equal(new[] { "Order" }, records);
        }

        [Fact]
        public void GetTokenRecordNames_LineIndexVariant_YieldsRecord()
        {
            // The line index is captured separately from the record
            string[] records = ZplRenderService.GetTokenRecordNames("$PurchLine.ItemId[2]$");
            Assert.Equal(new[] { "PurchLine" }, records);
        }

        [Fact]
        public void GetTokenRecordNames_FormatVariant_YieldsRecord()
        {
            // The format suffix is captured separately from the record
            string[] records = ZplRenderService.GetTokenRecordNames("$PurchLine.ItemId:..10$");
            Assert.Equal(new[] { "PurchLine" }, records);
        }

        [Fact]
        public void GetTokenRecordNames_AllVariantsCombined_YieldsRecord()
        {
            // All variants combined still yield the record
            string[] records = ZplRenderService.GetTokenRecordNames("$Line.Item()[2]:fmt$");
            Assert.Equal(new[] { "Line" }, records);
        }

        [Fact]
        public void GetTokenRecordNames_DoubleDollarLiteral_ProducesNoRecord()
        {
            // A double dollar is a literal passthrough and never a token
            string[] none = ZplRenderService.GetTokenRecordNames("literal $$ passthrough");
            Assert.Empty(none);

            string[] mixed = ZplRenderService.GetTokenRecordNames("$Acct.Bal$ price is $$5");
            Assert.Equal(new[] { "Acct" }, mixed);
        }

        [Fact]
        public void GetTokenRecordNames_PreservesCase_AndTreatsDifferentCaseAsDistinct()
        {
            // Record names are case-sensitive and distinct
            string[] records = ZplRenderService.GetTokenRecordNames("$REC.Field$ $rec.field$");

            Assert.Equal(2, records.Length);
            Assert.Contains("REC", records);
            Assert.Contains("rec", records);
        }

        // ComputeHash known vector

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

        // Render dimension edge cases

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
            // Explicit dimensions bypass the ^PW and ^LL parse entirely
            const string garbagePw = "^XA^PW0^FO50,50^A0N,40,40^FDHi^FS^XZ";
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(garbagePw, 8, 101.6, 50.8);

            Assert.Single(pngs);
            AssertPng(pngs[0]);
        }

        // Merged-artifact invariant, checked only when the deployed DLL exists

        [Fact]
        public void MergedArtifact_HasNoUnmergedThirdPartyReferences()
        {
            const string mergedPath =
                @"C:\AOSService\PackagesLocalDirectory\AAXWarehouseTools\bin\AtomicAx.Zpl.Render.dll";

            if (!File.Exists(mergedPath))
            {
                // Nothing to check when the deployed artifact is absent
                return;
            }

            // Read the bytes so the file is never locked
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

        // A configuration-only preamble block must not yield a blank label
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
            // A configuration-only ZPL still renders its block rather than returning nothing
            const string configOnly = "^XA~SD15^PR8,8^MNW^MTT^XZ";
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(configOnly, 8, 101.6, 50.8);

            Assert.True(pngs.Count >= 1);
        }
    }
}
