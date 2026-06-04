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

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectoryW(string lpPathName);

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

                // Version-mismatch safety net (zero web.config edits): when the host process has
                // no binding redirect for one of OUR payload assemblies (e.g. SkiaSharp's net472
                // build requests System.Runtime.CompilerServices.Unsafe 4.0.4.1 while a newer one
                // is deployed; or zxing 0.16.11 vs ER's loaded 0.16.5), .NET Framework's strict
                // bind fails first — only THEN does this handler serve the model-bin copy.
                // Normal/AOS-provided bindings are never overridden.
                AppDomain.CurrentDomain.AssemblyResolve += ResolveFromDeployFolder;
                log.AppendLine("AssemblyResolve fallback registered for the deployment folder.");

                log.AppendLine("Process bitness: " + (Environment.Is64BitProcess ? "x64" : "x86"));
                log.AppendLine("BaseDirectory: " + SafeString(() => AppDomain.CurrentDomain.BaseDirectory));
                log.AppendLine("Assembly.Location dir: " + SafeLocationDirectory());
                log.AppendLine("Assembly.CodeBase dir: " + SafeCodeBaseDirectory());

                // Belt-and-braces for SkiaSharp's OWN path-based loader: add the deployment
                // folder to the process DLL search path so even its internal probing resolves.
                string deployDir = SafeCodeBaseDirectory() ?? SafeLocationDirectory();
                if (!string.IsNullOrEmpty(deployDir))
                {
                    bool setOk = SetDllDirectoryW(deployDir);
                    log.AppendLine("SetDllDirectory(" + deployDir + "): " + (setOk ? "OK" : ("FAILED Win32 " + Marshal.GetLastWin32Error())));
                }

                foreach (string module in NativeModules)
                {
                    LoadModule(module, log);
                }

                foreach (string module in NativeModules)
                {
                    log.AppendLine(module + " in-process after preload: " + (GetModuleHandleW(module) != IntPtr.Zero));
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

            // ARCH-AWARE (U-1 round 3): pick the runtimes RID folder matching the PROCESS
            // bitness — loading an x86 native into the 64-bit AOS fails with Win32 error 193.
            string rid = Environment.Is64BitProcess ? "win-x64" : "win-x86";
            string nativeSubPath = Path.Combine("runtimes", Path.Combine(rid, Path.Combine("native", module)));

            return new[]
            {
                // CodeBase = original deployment folder (the model bin) — survives IIS shadow copy.
                Combine(codeBaseDir, nativeSubPath),
                // Location = shadow-copy dir under IIS, real dir under tests/console.
                Combine(locationDir, nativeSubPath),
                // Legacy flat layouts, last resort.
                Combine(codeBaseDir, module),
                Combine(locationDir, module),
                Combine(AppDomain.CurrentDomain.BaseDirectory, module)
            };
        }

        private static string Combine(string directory, string file)
        {
            return string.IsNullOrEmpty(directory) ? file : Path.Combine(directory, file);
        }

        private static string SafeString(Func<string> getter)
        {
            try { return getter(); }
            catch (Exception ex) { return "<error: " + ex.Message + ">"; }
        }

        private static Assembly ResolveFromDeployFolder(object sender, ResolveEventArgs args)
        {
            try
            {
                string simpleName = new AssemblyName(args.Name).Name;
                string deployDir = SafeCodeBaseDirectory() ?? SafeLocationDirectory();
                if (string.IsNullOrEmpty(deployDir))
                {
                    return null;
                }

                string candidate = Path.Combine(deployDir, simpleName + ".dll");
                if (!File.Exists(candidate))
                {
                    return null;
                }

                return Assembly.LoadFrom(candidate);
            }
            catch
            {
                return null;
            }
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
