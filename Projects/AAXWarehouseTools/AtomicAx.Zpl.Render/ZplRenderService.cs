using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BinaryKits.Zpl.Viewer;
using BinaryKits.Zpl.Viewer.ElementDrawers;
using BinaryKits.Zpl.Viewer.Models;
using SkiaSharp;

namespace AtomicAx.Zpl.Render
{
    /// <summary>
    /// In-process ZPL -> PNG rendering service wrapping BinaryKits.Zpl.Viewer 1.3.1
    /// (SkiaSharp-based, zero data egress — REQ-R-1/-3, C-9). The shared implementation of
    /// the SHA-256 ZPL hash (III.9) and the read-only token-record discovery scan (III.8/F7)
    /// also live here so X++ and the xunit tests use one implementation.
    /// </summary>
    public static class ZplRenderService
    {
        /// <summary>
        /// U-1 / IIS shadow copy: pre-load the win-x64 natives from the real deployment folder
        /// before any SkiaSharp P/Invoke. The type initializer runs before any member call,
        /// so every public entry point is covered.
        /// </summary>
        static ZplRenderService()
        {
            NativeLibraryPreloader.EnsureLoaded();
        }

        // Verified token grammar from plan F7 / WhsDocumentRoutingTranslator L16.
        // $Record.Field()[lineIndex]:format$  — Record is optional.
        private static readonly Regex TokenRegex = new Regex(
            @"\$(?:(?<record>[a-zA-Z0-9_]+?)\.)?(?<field>[a-zA-Z0-9_]+?)(?<methodIndicator>\(\))?(?:\[(?<lineIndex>[0-9]{1,3})\])?(?::(?<format>.*?))?\$",
            RegexOptions.Compiled);

        // First-occurrence-wins parsers for ^PW<dots> (print width) and ^LL<dots> (label length).
        private static readonly Regex PrintWidthRegex = new Regex(
            @"\^PW(?<dots>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LabelLengthRegex = new Regex(
            @"\^LL(?<dots>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Renders one PNG per ^XA…^XZ label block in the supplied ZPL.
        /// REQ-R-1:
        ///  - dpmm &lt;= 0  -&gt; ZplDimensionsMissingException (density must come from the caller).
        ///  - widthMm/heightMm &lt;= 0 -&gt; parse ^PW (width dots) / ^LL (length dots) and convert
        ///    via mm = dots / dpmm; if either command is absent -&gt; ZplDimensionsMissingException.
        ///  - empty/whitespace zpl -&gt; ZplRenderException.
        ///  - any analyzer/drawer failure -&gt; ZplRenderException (original message preserved).
        /// </summary>
        public static IList<byte[]> RenderToPngList(string zpl, int dpmm = 0, double widthMm = 0, double heightMm = 0)
        {
            if (string.IsNullOrWhiteSpace(zpl))
            {
                throw new ZplRenderException("ZPL is empty or whitespace.");
            }

            if (dpmm <= 0)
            {
                throw new ZplDimensionsMissingException(
                    "Print density (dpmm) was not supplied; the caller must provide a positive value.");
            }

            // Resolve dimensions: explicit override wins, otherwise parse ^PW / ^LL.
            if (widthMm <= 0 || heightMm <= 0)
            {
                int widthDots;
                int lengthDots;

                if (!TryParseFirst(PrintWidthRegex, zpl, out widthDots))
                {
                    throw new ZplDimensionsMissingException(
                        "Label width could not be determined: no widthMm supplied and no ^PW command found in the ZPL.");
                }

                if (!TryParseFirst(LabelLengthRegex, zpl, out lengthDots))
                {
                    throw new ZplDimensionsMissingException(
                        "Label height could not be determined: no heightMm supplied and no ^LL command found in the ZPL.");
                }

                if (widthMm <= 0)
                {
                    widthMm = (double)widthDots / dpmm;
                }

                if (heightMm <= 0)
                {
                    heightMm = (double)lengthDots / dpmm;
                }
            }

            try
            {
                IPrinterStorage printerStorage = new PrinterStorage();
                IZplAnalyzer analyzer = new ZplAnalyzer(printerStorage, new FormatMerger());
                AnalyzeInfo analyzeInfo = analyzer.Analyze(zpl);

                DrawerOptions options = new DrawerOptions
                {
                    RenderFormat = SKEncodedImageFormat.Png
                };
                ZplElementDrawer drawer = new ZplElementDrawer(printerStorage, options);

                List<byte[]> pngs = new List<byte[]>();
                if (analyzeInfo != null && analyzeInfo.LabelInfos != null)
                {
                    foreach (LabelInfo labelInfo in analyzeInfo.LabelInfos)
                    {
                        // One PNG per ^XA…^XZ block (each LabelInfo == one label).
                        byte[] png = drawer.Draw(labelInfo.ZplElements, widthMm, heightMm, dpmm);
                        pngs.Add(png);
                    }
                }

                return pngs;
            }
            catch (ZplRenderException)
            {
                // Already shaped; let it propagate untouched.
                throw;
            }
            catch (Exception ex)
            {
                string message = "ZPL rendering failed: " + ex.Message;

                // Native-load failures get the preloader's probe log appended so the X++ error
                // dialog shows exactly where the natives were (not) found (U-1 diagnosability).
                if (ex is DllNotFoundException || ex is TypeInitializationException
                    || ex.Message.IndexOf("libSkiaSharp", StringComparison.OrdinalIgnoreCase) >= 0
                    || ex.Message.IndexOf("libHarfBuzzSharp", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    message += Environment.NewLine + "Native preloader log:" + Environment.NewLine
                        + NativeLibraryPreloader.Diagnostics;
                }

                throw new ZplRenderException(message, ex);
            }
        }

        /// <summary>
        /// X++-friendly entry point (X++ cannot declare IList&lt;byte[]&gt;). Same semantics as
        /// <see cref="RenderToPngList(string,int,double,double)"/>, returning a ZplRenderResult.
        /// THIS is the API X++ calls.
        /// </summary>
        public static ZplRenderResult RenderToPngs(string zpl, int dpmm = 0, double widthMm = 0, double heightMm = 0)
        {
            IList<byte[]> pngs = RenderToPngList(zpl, dpmm, widthMm, heightMm);
            return new ZplRenderResult(pngs);
        }

        /// <summary>
        /// SHA-256 of the UTF-8 bytes of the ZPL, returned as lowercase hex (64 chars). III.9.
        /// </summary>
        public static string ComputeHash(string zpl)
        {
            if (zpl == null)
            {
                zpl = string.Empty;
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(zpl));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Read-only token discovery (C-5 allows discovery, not substitution). Returns the
        /// distinct, non-empty 'record' capture groups from the verified token regex (III.8/F7).
        /// Record-less tokens such as $OrderNum$ contribute no entry.
        /// </summary>
        public static string[] GetTokenRecordNames(string zpl)
        {
            if (string.IsNullOrEmpty(zpl))
            {
                return new string[0];
            }

            List<string> records = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in TokenRegex.Matches(zpl))
            {
                Group recordGroup = match.Groups["record"];
                if (recordGroup.Success)
                {
                    string record = recordGroup.Value;
                    if (!string.IsNullOrEmpty(record) && seen.Add(record))
                    {
                        records.Add(record);
                    }
                }
            }

            return records.ToArray();
        }

        private static bool TryParseFirst(Regex regex, string zpl, out int dots)
        {
            Match match = regex.Match(zpl);
            if (match.Success)
            {
                return int.TryParse(match.Groups["dots"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out dots);
            }

            dots = 0;
            return false;
        }
    }
}
