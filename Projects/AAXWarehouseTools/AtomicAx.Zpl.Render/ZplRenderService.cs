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
    /// (SkiaSharp-based, zero data egress). The shared implementation of
    /// the SHA-256 ZPL hash and the read-only token-record discovery scan
    /// also live here so X++ and the xunit tests use one implementation.
    /// </summary>
    public static class ZplRenderService
    {
        /// <summary>
        /// IIS shadow copy: pre-load the win-x64 natives from the real deployment folder
        /// before any SkiaSharp P/Invoke. The type initializer runs before any member call,
        /// so every public entry point is covered.
        /// </summary>
        static ZplRenderService()
        {
            NativeLibraryPreloader.EnsureLoaded();
        }

        // Verified token grammar matching WhsDocumentRoutingTranslator.
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
        /// Dimension resolution:
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

                List<byte[]> rendered = new List<byte[]>();
                if (analyzeInfo != null && analyzeInfo.LabelInfos != null)
                {
                    foreach (LabelInfo labelInfo in analyzeInfo.LabelInfos)
                    {
                        // One PNG per ^XA…^XZ block.
                        rendered.Add(drawer.Draw(labelInfo.ZplElements, widthMm, heightMm, dpmm));
                    }
                }

                // A ^XA…^XZ block that is purely printer CONFIGURATION (e.g.
                // ^XA~SD15^PR8,8^MNW^MTT…^XZ — darkness/print-rate/media setup) draws nothing
                // and renders BLANK (a single uniform color). Drop those so the preview shows
                // only real labels, not spurious blank images. Fallback: if every block is blank
                // (degenerate, all-config ZPL) keep them all rather than returning nothing.
                List<byte[]> printable = new List<byte[]>();
                foreach (byte[] png in rendered)
                {
                    if (!IsBlankPng(png))
                    {
                        printable.Add(png);
                    }
                }

                return printable.Count > 0 ? printable : rendered;
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
                // dialog shows exactly where the natives were (not) found, for diagnosability.
                if (ex is DllNotFoundException || ex is TypeInitializationException
                    || ex.Message.IndexOf("libSkiaSharp", StringComparison.OrdinalIgnoreCase) >= 0
                    || ex.Message.IndexOf("libHarfBuzzSharp", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    message += Environment.NewLine + "Native preloader log:" + Environment.NewLine
                        + NativeLibraryPreloader.Diagnostics
                        + Environment.NewLine + BuildSkiaLoadFailureReport(ex);
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
        /// Rotates a PNG image CLOCKWISE by 90°×<paramref name="quarterTurnsClockwise"/>, returning a
        /// re-encoded PNG. The integration contract with X++ AAXZplRenderService.rotatePng:
        ///  - turns = (((quarterTurnsClockwise % 4) + 4) % 4) — negative inputs normalize (e.g. -1 == 3).
        ///  - turns == 0 -&gt; the SAME byte[] reference is returned unchanged (no decode/encode round-trip).
        ///  - null/empty <paramref name="png"/> -&gt; ZplRenderException.
        ///  - odd turns swap width/height; even turns preserve them.
        ///  - any decode/encode failure -&gt; ZplRenderException (native-load failures get the preloader
        ///    log appended, mirroring RenderToPngList for diagnosability).
        /// SkiaSharp 3.119 surface used (verified against the package): SKBitmap.Decode(byte[]),
        /// new SKBitmap(int,int), new SKCanvas(SKBitmap), SKCanvas.Translate(float,float),
        /// SKCanvas.RotateDegrees(float), SKCanvas.DrawBitmap(SKBitmap,float,float,SKPaint),
        /// SKImage.FromBitmap(SKBitmap), SKImage.Encode(SKEncodedImageFormat.Png,100), SKData.ToArray().
        /// </summary>
        public static byte[] RotatePng(byte[] png, int quarterTurnsClockwise)
        {
            if (png == null || png.Length == 0)
            {
                throw new ZplRenderException("Cannot rotate a null or empty PNG.");
            }

            int turns = ((quarterTurnsClockwise % 4) + 4) % 4;
            if (turns == 0)
            {
                // Contract: turns==0 returns the SAME byte array reference, untouched.
                return png;
            }

            // The type initializer already ran the native preloader, but be explicit (cheap, idempotent).
            NativeLibraryPreloader.EnsureLoaded();

            try
            {
                using (SKBitmap source = SKBitmap.Decode(png))
                {
                    if (source == null)
                    {
                        throw new ZplRenderException("PNG could not be decoded for rotation (SKBitmap.Decode returned null).");
                    }

                    // Odd quarter-turns swap the dimensions.
                    bool swap = (turns % 2) != 0;
                    int destWidth = swap ? source.Height : source.Width;
                    int destHeight = swap ? source.Width : source.Height;

                    using (SKBitmap rotated = new SKBitmap(destWidth, destHeight, source.ColorType, source.AlphaType))
                    using (SKCanvas canvas = new SKCanvas(rotated))
                    {
                        canvas.Clear(SKColors.Transparent);

                        // Place the rotation origin so the source lands inside the destination after a
                        // CLOCKWISE rotation (Skia: positive degrees rotate clockwise, +y is down).
                        switch (turns)
                        {
                            case 1: // 90° CW
                                canvas.Translate(destWidth, 0);
                                break;
                            case 2: // 180°
                                canvas.Translate(destWidth, destHeight);
                                break;
                            case 3: // 270° CW
                                canvas.Translate(0, destHeight);
                                break;
                        }

                        canvas.RotateDegrees(90f * turns);
                        canvas.DrawBitmap(source, 0, 0, null);
                        canvas.Flush();

                        using (SKImage image = SKImage.FromBitmap(rotated))
                        using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
                        {
                            if (data == null)
                            {
                                throw new ZplRenderException("Rotated image could not be encoded to PNG (SKImage.Encode returned null).");
                            }

                            return data.ToArray();
                        }
                    }
                }
            }
            catch (ZplRenderException)
            {
                throw;
            }
            catch (Exception ex)
            {
                string message = "PNG rotation failed: " + ex.Message;

                if (ex is DllNotFoundException || ex is TypeInitializationException
                    || ex.Message.IndexOf("libSkiaSharp", StringComparison.OrdinalIgnoreCase) >= 0
                    || ex.Message.IndexOf("libHarfBuzzSharp", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    message += Environment.NewLine + "Native preloader log:" + Environment.NewLine
                        + NativeLibraryPreloader.Diagnostics
                        + Environment.NewLine + BuildSkiaLoadFailureReport(ex);
                }

                throw new ZplRenderException(message, ex);
            }
        }

        /// <summary>
        /// Runtime environment report for support diagnostics:
        /// native preloader probe log + loaded renderer assembly identities.
        /// </summary>
        public static string GetRuntimeDiagnostics()
        {
            NativeLibraryPreloader.EnsureLoaded();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("== Native preloader ==");
            sb.AppendLine(NativeLibraryPreloader.Diagnostics);
            sb.AppendLine("== Managed identities ==");
            sb.AppendLine("AtomicAx.Zpl.Render: " + typeof(ZplRenderService).Assembly.FullName);
            sb.AppendLine("SkiaSharp: " + typeof(SKBitmap).Assembly.FullName + " @ " + typeof(SKBitmap).Assembly.Location);
            sb.AppendLine("BinaryKits.Zpl.Viewer: " + typeof(ZplAnalyzer).Assembly.FullName);
            sb.AppendLine("ZXing: " + typeof(ZXing.BarcodeWriterPixelData).Assembly.FullName + " @ " + typeof(ZXing.BarcodeWriterPixelData).Assembly.Location);
            return sb.ToString();
        }

        /// <summary>
        /// SHA-256 of the UTF-8 bytes of the ZPL, returned as lowercase hex (64 chars).
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
        /// Read-only token discovery (discovery only, not substitution). Returns the
        /// distinct, non-empty 'record' capture groups from the verified token regex.
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

        /// <summary>
        /// Ground-truth report for native-load failures: WHICH SkiaSharp instance was executing
        /// (load contexts can duplicate identity-equal assemblies), every loaded SkiaSharp, and
        /// on-disk existence for the loader's exact candidate paths (Location dir and
        /// BaseDirectory, flat + x64 subdir — per SkiaSharp 3.119 LibraryLoader source).
        /// </summary>
        private static string BuildSkiaLoadFailureReport(Exception ex)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("== Skia load-failure report ==");

            try
            {
                Exception innermost = ex;
                while (innermost.InnerException != null)
                {
                    innermost = innermost.InnerException;
                }

                System.Reflection.Assembly throwing = null;
                if (innermost.TargetSite != null && innermost.TargetSite.DeclaringType != null)
                {
                    throwing = innermost.TargetSite.DeclaringType.Assembly;
                }

                sb.AppendLine("Throwing site: " + (innermost.TargetSite == null ? "<unknown>" :
                    innermost.TargetSite.DeclaringType + "." + innermost.TargetSite.Name));
                sb.AppendLine("EXECUTING assembly: " + Describe(throwing));
                sb.AppendLine("Compile-time-bound SkiaSharp: " + Describe(typeof(SKBitmap).Assembly));

                int skiaCount = 0;
                foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name = null;
                    try { name = a.GetName().Name; } catch { }
                    if (string.Equals(name, "SkiaSharp", StringComparison.OrdinalIgnoreCase))
                    {
                        skiaCount++;
                        sb.AppendLine("Loaded SkiaSharp #" + skiaCount + ": " + Describe(a));
                    }
                }
                sb.AppendLine("SkiaSharp instances in AppDomain: " + skiaCount);

                string execDir = null;
                try { execDir = (throwing == null || string.IsNullOrEmpty(throwing.Location)) ? null : System.IO.Path.GetDirectoryName(throwing.Location); } catch { }
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                foreach (string root in new[] { execDir, baseDir })
                {
                    if (string.IsNullOrEmpty(root)) { sb.AppendLine("candidate root: <empty>"); continue; }
                    foreach (string candidate in new[]
                    {
                        System.IO.Path.Combine(root, System.IO.Path.Combine("x64", "libSkiaSharp.dll")),
                        System.IO.Path.Combine(root, "libSkiaSharp.dll")
                    })
                    {
                        sb.AppendLine("exists " + candidate + ": " + System.IO.File.Exists(candidate));
                    }
                }
            }
            catch (Exception reportEx)
            {
                sb.AppendLine("<report failed: " + reportEx.Message + ">");
            }

            return sb.ToString();
        }

        private static string Describe(System.Reflection.Assembly assembly)
        {
            if (assembly == null)
            {
                return "<null>";
            }

            string location;
            try { location = assembly.Location; } catch (Exception e) { location = "<" + e.Message + ">"; }
            if (string.IsNullOrEmpty(location))
            {
                location = "<EMPTY Location — byte-loaded>";
            }

            string codeBase;
            try { codeBase = assembly.CodeBase; } catch { codeBase = "<n/a>"; }

            return assembly.FullName + " | Location=" + location + " | CodeBase=" + codeBase;
        }

        /// <summary>
        /// True if a rendered PNG is visually blank — every pixel is the same color (nothing was
        /// drawn). Used to drop configuration-only ^XA…^XZ blocks from the preview. Scans the raw
        /// decoded pixel buffer and early-exits on the first differing pixel, so real labels
        /// (which have a barcode/text near the top) return false almost immediately.
        /// </summary>
        private static bool IsBlankPng(byte[] png)
        {
            if (png == null || png.Length == 0)
            {
                return true;
            }

            using (SKBitmap bitmap = SKBitmap.Decode(png))
            {
                if (bitmap == null)
                {
                    // Undecodable — don't silently drop it; treat as non-blank (keep).
                    return false;
                }

                byte[] pixels = bitmap.Bytes;
                int bpp = bitmap.BytesPerPixel;
                if (pixels == null || pixels.Length < bpp || bpp <= 0)
                {
                    return false;
                }

                for (int i = bpp; i + bpp <= pixels.Length; i += bpp)
                {
                    for (int b = 0; b < bpp; b++)
                    {
                        if (pixels[i + b] != pixels[b])
                        {
                            return false; // a pixel differs from the first → real content
                        }
                    }
                }

                return true; // uniform color → blank
            }
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
