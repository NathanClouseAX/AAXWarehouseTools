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
    /// Renders ZPL to PNG images in-process using BinaryKits.Zpl.Viewer and SkiaSharp. Also provides
    /// the shared SHA-256 ZPL hash and the read-only token-record scan so X++ and the tests use one
    /// implementation.
    /// </summary>
    public static class ZplRenderService
    {
        /// <summary>
        /// Pre-loads the native libraries before any SkiaSharp call so every public entry point is covered.
        /// </summary>
        static ZplRenderService()
        {
            NativeLibraryPreloader.EnsureLoaded();
        }

        // Token grammar used by the document routing translator: $Record.Field()[lineIndex]:format$
        private static readonly Regex TokenRegex = new Regex(
            @"\$(?:(?<record>[a-zA-Z0-9_]+?)\.)?(?<field>[a-zA-Z0-9_]+?)(?<methodIndicator>\(\))?(?:\[(?<lineIndex>[0-9]{1,3})\])?(?::(?<format>.*?))?\$",
            RegexOptions.Compiled);

        // Parsers for the ^PW (print width) and ^LL (label length) commands
        private static readonly Regex PrintWidthRegex = new Regex(
            @"\^PW(?<dots>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LabelLengthRegex = new Regex(
            @"\^LL(?<dots>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Renders one PNG per ^XA…^XZ label block in the supplied ZPL. When no explicit size is
        /// supplied, the label width and height are parsed from the ^PW and ^LL commands.
        /// </summary>
        /// <param name="zpl">The ZPL to render.</param>
        /// <param name="dpmm">The print density in dots per millimeter; must be positive.</param>
        /// <param name="widthMm">The label width in millimeters, or 0 to parse ^PW from the ZPL.</param>
        /// <param name="heightMm">The label height in millimeters, or 0 to parse ^LL from the ZPL.</param>
        /// <returns>The rendered PNG images, one per label block.</returns>
        /// <exception cref="ZplDimensionsMissingException">The density is not positive, or a dimension is neither supplied nor declared in the ZPL.</exception>
        /// <exception cref="ZplRenderException">The ZPL is empty or the renderer fails.</exception>
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

            // Explicit dimensions win, otherwise parse ^PW and ^LL
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
                        // One PNG per ^XA…^XZ block
                        rendered.Add(drawer.Draw(labelInfo.ZplElements, widthMm, heightMm, dpmm));
                    }
                }

                // Drop blank configuration-only blocks unless every block is blank
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
                // Already shaped, so propagate untouched
                throw;
            }
            catch (Exception ex)
            {
                string message = "ZPL rendering failed: " + ex.Message;

                // Append the preloader log to native-load failures for diagnostics
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
        /// Renders the supplied ZPL with the same semantics as <see cref="RenderToPngList(string,int,double,double)"/>
        /// and returns the images as a <see cref="ZplRenderResult"/>. This is the entry point X++ calls.
        /// </summary>
        /// <param name="zpl">The ZPL to render.</param>
        /// <param name="dpmm">The print density in dots per millimeter; must be positive.</param>
        /// <param name="widthMm">The label width in millimeters, or 0 to parse ^PW from the ZPL.</param>
        /// <param name="heightMm">The label height in millimeters, or 0 to parse ^LL from the ZPL.</param>
        /// <returns>The rendered images wrapped for X++ consumption.</returns>
        public static ZplRenderResult RenderToPngs(string zpl, int dpmm = 0, double widthMm = 0, double heightMm = 0)
        {
            IList<byte[]> pngs = RenderToPngList(zpl, dpmm, widthMm, heightMm);
            return new ZplRenderResult(pngs);
        }

        /// <summary>
        /// Rotates a PNG image clockwise by the given number of quarter turns and returns a re-encoded
        /// PNG. Negative values are normalized, zero turns returns the same array unchanged, and odd
        /// turns swap the width and height.
        /// </summary>
        /// <param name="png">The PNG image to rotate.</param>
        /// <param name="quarterTurnsClockwise">The number of 90-degree clockwise turns; negative values rotate counter-clockwise.</param>
        /// <returns>The rotated PNG, or the original array when no rotation is required.</returns>
        /// <exception cref="ZplRenderException">The image is null or empty, or it cannot be decoded or encoded.</exception>
        public static byte[] RotatePng(byte[] png, int quarterTurnsClockwise)
        {
            if (png == null || png.Length == 0)
            {
                throw new ZplRenderException("Cannot rotate a null or empty PNG.");
            }

            int turns = ((quarterTurnsClockwise % 4) + 4) % 4;
            if (turns == 0)
            {
                // Zero turns returns the same array reference
                return png;
            }

            // Ensure the native libraries are loaded
            NativeLibraryPreloader.EnsureLoaded();

            try
            {
                using (SKBitmap source = SKBitmap.Decode(png))
                {
                    if (source == null)
                    {
                        throw new ZplRenderException("PNG could not be decoded for rotation (SKBitmap.Decode returned null).");
                    }

                    // Odd quarter turns swap the dimensions
                    bool swap = (turns % 2) != 0;
                    int destWidth = swap ? source.Height : source.Width;
                    int destHeight = swap ? source.Width : source.Height;

                    using (SKBitmap rotated = new SKBitmap(destWidth, destHeight, source.ColorType, source.AlphaType))
                    using (SKCanvas canvas = new SKCanvas(rotated))
                    {
                        canvas.Clear(SKColors.Transparent);

                        // Translate so the source lands inside the destination after a clockwise rotation
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
        /// Builds a runtime report for support diagnostics containing the native preloader log and the
        /// identities of the loaded renderer assemblies.
        /// </summary>
        /// <returns>The diagnostics report.</returns>
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
        /// Computes the SHA-256 hash of the UTF-8 bytes of the ZPL.
        /// </summary>
        /// <param name="zpl">The ZPL to hash; null is treated as an empty string.</param>
        /// <returns>The hash as 64 lowercase hexadecimal characters.</returns>
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
        /// Scans the ZPL for tokens and returns the distinct record names they reference. Tokens without
        /// a record qualifier, such as $OrderNum$, contribute no entry.
        /// </summary>
        /// <param name="zpl">The ZPL to scan.</param>
        /// <returns>The distinct record names in order of first appearance.</returns>
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
        /// Builds a report for native-load failures listing the assembly that threw, every loaded
        /// SkiaSharp assembly, and whether the native library exists at the loader's candidate paths.
        /// </summary>
        /// <param name="ex">The exception raised by the failed render or rotation.</param>
        /// <returns>The report text.</returns>
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

        /// <summary>
        /// Describes an assembly by its full name, location, and code base for diagnostics.
        /// </summary>
        /// <param name="assembly">The assembly to describe, or null.</param>
        /// <returns>The description, or a placeholder when the assembly is null.</returns>
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
        /// Determines whether a rendered PNG is blank, meaning every pixel has the same color. Used to
        /// drop configuration-only ^XA…^XZ blocks from the preview.
        /// </summary>
        /// <param name="png">The PNG image to inspect.</param>
        /// <returns>True if the image is empty or uniform; otherwise false.</returns>
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
                    // Keep undecodable images rather than dropping them silently
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

        /// <summary>
        /// Parses the dot count from the first match of a dimension command in the ZPL.
        /// </summary>
        /// <param name="regex">The ^PW or ^LL parser.</param>
        /// <param name="zpl">The ZPL to search.</param>
        /// <param name="dots">The parsed dot count, or 0 when not found.</param>
        /// <returns>True if the command was found and parsed; otherwise false.</returns>
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
