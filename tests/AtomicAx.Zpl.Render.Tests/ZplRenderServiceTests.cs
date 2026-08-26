using System;
using System.Collections.Generic;
using System.Linq;
using AtomicAx.Zpl.Render;
using SkiaSharp;
using Xunit;

namespace AtomicAx.Zpl.Render.Tests
{
    /// <summary>
    /// Tests the rendering, hashing, token-scan, and rotation surface of <see cref="ZplRenderService"/>.
    /// </summary>
    public class ZplRenderServiceTests
    {
        // 4x2 inch label at 8 dpmm: ^PW812 (101.6 mm) ^LL406 (50.8 mm)
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

        /// <summary>
        /// Asserts that the bytes are a non-empty PNG image by checking the magic header.
        /// </summary>
        /// <param name="bytes">The bytes to check.</param>
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
            // Explicit dimensions render a label that declares none
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(NoDims, 8, 101.6, 50.8);

            Assert.Single(pngs);
            AssertPng(pngs[0]);
        }

        [Fact]
        public void GarbageInput_ThrowsZplRenderException_NotNullReference()
        {
            // Whitespace input is rejected as a render exception
            Assert.Throws<ZplRenderException>(
                () => ZplRenderService.RenderToPngList("   ", 8));

            // Non-ZPL text yields a shaped exception rather than a null reference
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
            // Exercises the ZXing barcode path
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
            // The well-known SHA-256 digest of the empty string
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
            // A record-less token contributes no entry
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

        // RotatePng contract shared with the X++ AAXZplRenderService.rotatePng wrapper

        /// <summary>
        /// Renders the 4x2 inch label to a single PNG for the rotation tests.
        /// </summary>
        /// <returns>The rendered PNG bytes.</returns>
        private static byte[] RenderSinglePng()
        {
            IList<byte[]> pngs = ZplRenderService.RenderToPngList(Valid4x2, 8);
            Assert.Single(pngs);
            AssertPng(pngs[0]);
            return pngs[0];
        }

        /// <summary>
        /// Decodes a PNG with SkiaSharp and returns its dimensions in pixels.
        /// </summary>
        /// <param name="png">The PNG bytes to decode.</param>
        /// <returns>The width and height of the image.</returns>
        private static (int Width, int Height) DecodeDimensions(byte[] png)
        {
            using (SKBitmap bmp = SKBitmap.Decode(png))
            {
                Assert.NotNull(bmp);
                return (bmp.Width, bmp.Height);
            }
        }

        [Fact]
        public void RotatePng_OneTurn_SwapsDimensions_AndStaysValidPng()
        {
            byte[] original = RenderSinglePng();
            (int w0, int h0) = DecodeDimensions(original);

            byte[] rotated = ZplRenderService.RotatePng(original, 1);

            AssertPng(rotated);
            (int w1, int h1) = DecodeDimensions(rotated);
            // An odd quarter turn swaps width and height
            Assert.Equal(h0, w1);
            Assert.Equal(w0, h1);
            // The label is not square, so the swap is observable
            Assert.NotEqual(w0, h0);
        }

        [Fact]
        public void RotatePng_TwoTurns_PreservesDimensions()
        {
            byte[] original = RenderSinglePng();
            (int w0, int h0) = DecodeDimensions(original);

            byte[] rotated = ZplRenderService.RotatePng(original, 2);

            AssertPng(rotated);
            (int w2, int h2) = DecodeDimensions(rotated);
            Assert.Equal(w0, w2);
            Assert.Equal(h0, h2);
        }

        [Fact]
        public void RotatePng_FourTurns_PreservesDimensions()
        {
            byte[] original = RenderSinglePng();
            (int w0, int h0) = DecodeDimensions(original);

            byte[] rotated = ZplRenderService.RotatePng(original, 4);

            AssertPng(rotated);
            (int w4, int h4) = DecodeDimensions(rotated);
            Assert.Equal(w0, w4);
            Assert.Equal(h0, h4);
        }

        [Fact]
        public void RotatePng_ZeroTurns_ReturnsSameReferenceUnchanged()
        {
            byte[] original = RenderSinglePng();

            byte[] result = ZplRenderService.RotatePng(original, 0);

            // Zero turns returns the same array reference
            Assert.Same(original, result);
        }

        [Fact]
        public void RotatePng_NegativeOne_EqualsThreeTurns_Dimensionally()
        {
            byte[] original = RenderSinglePng();

            byte[] minusOne = ZplRenderService.RotatePng(original, -1);
            byte[] three = ZplRenderService.RotatePng(original, 3);

            AssertPng(minusOne);
            AssertPng(three);
            (int wm, int hm) = DecodeDimensions(minusOne);
            (int w3, int h3) = DecodeDimensions(three);
            Assert.Equal(w3, wm);
            Assert.Equal(h3, hm);

            // Both equal the dimension swap of the original
            (int w0, int h0) = DecodeDimensions(original);
            Assert.Equal(h0, wm);
            Assert.Equal(w0, hm);
        }

        [Fact]
        public void RotatePng_NullOrEmpty_ThrowsZplRenderException()
        {
            Assert.Throws<ZplRenderException>(() => ZplRenderService.RotatePng(null, 1));
            Assert.Throws<ZplRenderException>(() => ZplRenderService.RotatePng(new byte[0], 1));
        }
    }
}
