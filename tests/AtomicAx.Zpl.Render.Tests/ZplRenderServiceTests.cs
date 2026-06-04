using System;
using System.Collections.Generic;
using System.Linq;
using AtomicAx.Zpl.Render;
using Xunit;

namespace AtomicAx.Zpl.Render.Tests
{
    public class ZplRenderServiceTests
    {
        // 4x2 inch label at 8 dpmm => ^PW812 (101.6mm) ^LL406 (50.8mm).
        private const string Valid4x2 =
            "^XA^PW812^LL406^FO50,50^A0N,40,40^FDHello^FS^XZ";

        private const string MultiLabel =
            "^XA^PW812^LL406^FO50,50^A0N,40,40^FDLabel One^FS^XZ" +
            "^XA^PW812^LL406^FO50,50^A0N,40,40^FDLabel Two^FS^XZ";

        private const string NoDims =
            "^XA^FO50,50^A0N,40,40^FDNoSize^FS^XZ";

        private const string Barcode =
            "^XA^PW812^LL406^BY3^BCN,100,Y,N,N^FD123456789^FS^XZ";

        private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47 };

        private static void AssertPng(byte[] bytes)
        {
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0, "PNG byte array must be non-empty.");
            Assert.True(bytes.Length >= 4, "PNG byte array too short for a magic header.");
            for (int i = 0; i < PngMagic.Length; i++)
            {
                Assert.Equal(PngMagic[i], bytes[i]);
            }
        }

        [Fact]
        public void Valid4x2_RendersExactlyOneNonEmptyPng()
        {
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(Valid4x2, 8);

            Assert.Single(pngs);
            AssertPng(pngs[0]);
        }

        [Fact]
        public void MultiLabel_RendersExactlyTwoPngs()
        {
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(MultiLabel, 8);

            Assert.Equal(2, pngs.Count);
            AssertPng(pngs[0]);
            AssertPng(pngs[1]);
        }

        [Fact]
        public void NoPwOrLl_AndNoExplicitDims_ThrowsDimensionsMissing()
        {
            Assert.Throws<ZplDimensionsMissingException>(
                () => ZplRenderService.RenderToPngList(NoDims, 8));
        }

        [Fact]
        public void DpmmZero_ThrowsDimensionsMissing()
        {
            Assert.Throws<ZplDimensionsMissingException>(
                () => ZplRenderService.RenderToPngList(Valid4x2, 0));
        }

        [Fact]
        public void ExplicitDims_OverrideParsing_WorksWithoutPwOrLl()
        {
            // No ^PW/^LL in the ZPL, but explicit width/height are supplied -> renders.
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(NoDims, 8, 101.6, 50.8);

            Assert.Single(pngs);
            AssertPng(pngs[0]);
        }

        [Fact]
        public void GarbageInput_ThrowsZplRenderException_NotNullReference()
        {
            // Whitespace / empty -> ZplRenderException (the empty-input guard).
            Assert.Throws<ZplRenderException>(
                () => ZplRenderService.RenderToPngList("   ", 8));

            // Non-ZPL text with no ^PW/^LL and no dims -> a shaped exception, never a raw NRE.
            Exception ex = Record.Exception(
                () => ZplRenderService.RenderToPngList("not zpl at all", 8));
            Assert.NotNull(ex);
            Assert.IsAssignableFrom<ZplRenderException>(ex);
            Assert.IsNotType<NullReferenceException>(ex);
        }

        [Fact]
        public void NullInput_ThrowsZplRenderException_NotNullReference()
        {
            Exception ex = Record.Exception(
                () => ZplRenderService.RenderToPngList(null, 8));
            Assert.NotNull(ex);
            Assert.IsAssignableFrom<ZplRenderException>(ex);
            Assert.IsNotType<NullReferenceException>(ex);
        }

        [Fact]
        public void Barcode_Code128_Renders()
        {
            // Exercises the ZXing path through BinaryKits.
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(Barcode, 8);

            Assert.Single(pngs);
            AssertPng(pngs[0]);
        }

        [Fact]
        public void RenderToPngs_XppWrapper_MatchesListResult()
        {
            ZplRenderResult result = ZplRenderService.RenderToPngs(MultiLabel, 8);

            Assert.Equal(2, result.Count);
            AssertPng(result.GetPng(0));
            AssertPng(result.GetPng(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => result.GetPng(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => result.GetPng(-1));
        }

        [Fact]
        public void ComputeHash_Is64LowercaseHexChars_AndStable()
        {
            string h1 = ZplRenderService.ComputeHash(Valid4x2);
            string h2 = ZplRenderService.ComputeHash(Valid4x2);

            Assert.Equal(64, h1.Length);
            Assert.Equal(h1, h2);
            Assert.All(h1, c => Assert.True(
                (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'),
                "Hash must be lowercase hex."));
        }

        [Fact]
        public void ComputeHash_DiffersForDifferentInput()
        {
            string h1 = ZplRenderService.ComputeHash(Valid4x2);
            string h2 = ZplRenderService.ComputeHash(MultiLabel);

            Assert.NotEqual(h1, h2);
        }

        [Fact]
        public void ComputeHash_KnownVector()
        {
            // SHA-256 of UTF-8 "" is the well-known empty-string digest.
            Assert.Equal(
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                ZplRenderService.ComputeHash(string.Empty));
        }

        [Fact]
        public void GetTokenRecordNames_ReturnsDistinctRecordsAndSkipsRecordlessTokens()
        {
            string zpl = "$WHSLicensePlateLabel.LicensePlateId$ $OrderNum$ $PurchLine_1.ItemId[2]:..10$";

            string[] records = ZplRenderService.GetTokenRecordNames(zpl);

            Assert.Contains("WHSLicensePlateLabel", records);
            Assert.Contains("PurchLine_1", records);
            // Record-less $OrderNum$ contributes no record entry.
            Assert.DoesNotContain("OrderNum", records);
        }

        [Fact]
        public void GetTokenRecordNames_Distinct()
        {
            string zpl = "$Rec.A$ $Rec.B$ $Rec.C$";

            string[] records = ZplRenderService.GetTokenRecordNames(zpl);

            Assert.Single(records);
            Assert.Equal("Rec", records[0]);
        }
    }
}
