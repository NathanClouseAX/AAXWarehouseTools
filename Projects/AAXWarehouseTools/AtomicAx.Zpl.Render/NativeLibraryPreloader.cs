using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace AtomicAx.Zpl.Render
{
    /// <summary>
    /// Pre-loads the win-x64 native assets (libSkiaSharp.dll, libHarfBuzzSharp.dll) before any
    /// SkiaSharp P/Invoke runs.
    ///
    /// Why (plan U-1, hit live in the AOS): IIS shadow-copies managed assemblies into a temp
    /// directory before loading them, so when SkiaSharp probes for its native library "next to
    /// itself" (Assembly.Location) it looks in the shadow-copy folder — where the native DLL is
    /// not. Assembly.CodeBase still points at the ORIGINAL deployment folder (the model bin),
    /// so we resolve the natives from there and load them explicitly with LoadLibrary. Once a
    /// module is in the process, subsequent DllImport("libSkiaSharp") binds to it by base name.
    ///
    /// If another model (e.g. ElectronicReporting's same-line SkiaSharp 3.119.0) already loaded
    /// a module with the same base name, we leave it alone (GetModuleHandle short-circuit) —
    /// loading a second copy under the same name would not win the DllImport bind anyway.
    /// </summary>
    internal static class NativeLibraryPreloader
    {
        private static readonly object Sync = new object();
        private static bool initialized;
        private static string diagnostics = string.Empty;

        private static readonly string[] NativeModules =
        {
            "libSkiaSharp.dll",
            "libHarfBuzzSharp.dll"
        };

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        /// <summary>
        /// Idempotent; safe to call from every public entry point.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (initialized)
            {
                return;
            }

            lock (Sync)
            {
                if (initialized)
                {
                    return;
                }

                StringBuilder log = new StringBuilder();

                foreach (string module in NativeModules)
                {
                    LoadModule(module, log);
                }

                diagnostics = log.ToString();
                initialized = true;
            }
        }

        /// <summary>
        /// Probe/load record for error messages and the WP-0.5 probe.
        /// </summary>
        public static string Diagnostics
        {
            get { return diagnostics; }
        }

        private static void LoadModule(string module, StringBuilder log)
        {
            if (GetModuleHandleW(module) != IntPtr.Zero)
            {
                log.AppendLine(module + ": already loaded in-process (left as-is).");
                return;
            }

            foreach (string candidate in CandidatePaths(module))
            {
                if (!File.Exists(candidate))
                {
                    log.AppendLine(module + ": not found at " + candidate);
                    continue;
                }

                if (LoadLibraryW(candidate) != IntPtr.Zero)
                {
                    log.AppendLine(module + ": loaded from " + candidate);
                    return;
                }

                int error = Marshal.GetLastWin32Error();
                log.AppendLine(module + ": LoadLibrary failed (Win32 error " + error + ") for " + candidate);
            }

            log.AppendLine(module + ": NOT loaded by preloader; DllImport will fall back to default probing.");
        }

        private static string[] CandidatePaths(string module)
        {
            string codeBaseDir = SafeCodeBaseDirectory();
            string locationDir = SafeLocationDirectory();

            return new[]
            {
                // CodeBase = original deployment folder (the model bin) — survives IIS shadow copy.
                Combine(codeBaseDir, module),
                Combine(codeBaseDir, Path.Combine("runtimes", Path.Combine("win-x64", Path.Combine("native", module)))),
                // Location = shadow-copy dir under IIS, real dir under tests/console.
                Combine(locationDir, module),
                Combine(locationDir, Path.Combine("runtimes", Path.Combine("win-x64", Path.Combine("native", module)))),
                Combine(AppDomain.CurrentDomain.BaseDirectory, module)
            };
        }

        private static string Combine(string directory, string file)
        {
            return string.IsNullOrEmpty(directory) ? file : Path.Combine(directory, file);
        }

        private static string SafeCodeBaseDirectory()
        {
            try
            {
                Assembly assembly = typeof(NativeLibraryPreloader).Assembly;
                Uri uri = new Uri(assembly.CodeBase);
                return Path.GetDirectoryName(uri.LocalPath);
            }
            catch
            {
                return null;
            }
        }

        private static string SafeLocationDirectory()
        {
            try
            {
                return Path.GetDirectoryName(typeof(NativeLibraryPreloader).Assembly.Location);
            }
            catch
            {
                return null;
            }
        }
    }
}
