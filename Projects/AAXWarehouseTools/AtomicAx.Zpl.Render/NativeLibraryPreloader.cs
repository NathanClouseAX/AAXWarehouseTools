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
    /// Why: IIS shadow-copies managed assemblies into a temp
    /// directory before loading them, so when SkiaSharp probes for its native library "next to
    /// itself" (Assembly.Location) it looks in the shadow-copy folder — where the native DLL is
    /// not. Assembly.CodeBase still points at the ORIGINAL deployment folder (the model bin),
    /// so we resolve the natives from there and load them explicitly with LoadLibrary. Once a
    /// module is in the process, subsequent DllImport("libSkiaSharp") binds to it by base name.
    ///
    /// If another model has already loaded a module with the same base name, we leave it alone
    /// (GetModuleHandle short-circuit) — loading a second copy under the same name would not win the
    /// DllImport bind anyway.
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

                    // Standalone single-DLL mode: the natives ship INSIDE this assembly as
                    // embedded resources. Extract beside the assembly (preferred — that is
                    // exactly where SkiaSharp's file-path-based LibraryLoader probes, and
                    // post-merge SkiaSharp's types LIVE in this assembly so its Location IS
                    // ours) or to %TEMP%, then LoadLibrary the extracted file.
                    if (GetModuleHandleW(module) == IntPtr.Zero)
                    {
                        ExtractAndLoadEmbedded(module, log);
                    }
                }

                // Legacy belt for UNMERGED layouts (e.g. the net8 test build or a dev deployment
                // of separate DLLs): SkiaSharp's managed loader probes beside its own
                // Assembly.Location, so stage the native file there. No-op when the module is
                // already in-process or the managed assembly is merged into this one.
                StageBesideManagedAssembly("SkiaSharp", "libSkiaSharp.dll", log);
                StageBesideManagedAssembly("HarfBuzzSharp", "libHarfBuzzSharp.dll", log);

                foreach (string module in NativeModules)
                {
                    log.AppendLine(module + " in-process after preload: " + (GetModuleHandleW(module) != IntPtr.Zero));
                }

                diagnostics = log.ToString();
                initialized = true;
            }
        }

        /// <summary>
        /// Probe/load record for error messages and support diagnostics.
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

            // ARCH-AWARE: pick the runtimes RID folder matching the PROCESS
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

        /// <summary>
        /// Extracts the embedded native (logical name AAX.Natives.&lt;rid&gt;.&lt;module&gt;) to a
        /// writable location and loads it. Targets, in order: beside Assembly.Location, beside
        /// Assembly.CodeBase, then %TEMP%\AtomicAx.Zpl.Render\&lt;version&gt;\&lt;rid&gt;\.
        /// </summary>
        private static void ExtractAndLoadEmbedded(string module, StringBuilder log)
        {
            try
            {
                string rid = Environment.Is64BitProcess ? "win-x64" : "win-x86";
                string resourceName = "AAX.Natives." + rid + "." + module;
                Assembly self = typeof(NativeLibraryPreloader).Assembly;

                byte[] payload;
                using (Stream stream = self.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        log.AppendLine(module + ": no embedded resource '" + resourceName + "'.");
                        return;
                    }

                    payload = new byte[stream.Length];
                    int offset = 0;
                    while (offset < payload.Length)
                    {
                        int read = stream.Read(payload, offset, payload.Length - offset);
                        if (read <= 0)
                        {
                            break;
                        }
                        offset += read;
                    }
                }

                string version;
                try { version = self.GetName().Version.ToString(); } catch { version = "0"; }

                string locationDir = SafeLocationDirectory();
                string codeBaseDir = SafeCodeBaseDirectory();
                string tempDir = Path.Combine(Path.GetTempPath(), Path.Combine("AtomicAx.Zpl.Render", Path.Combine(version, rid)));

                foreach (string targetDir in new[] { locationDir, codeBaseDir, tempDir })
                {
                    if (string.IsNullOrEmpty(targetDir))
                    {
                        continue;
                    }

                    try
                    {
                        Directory.CreateDirectory(targetDir);
                        string target = Path.Combine(targetDir, module);

                        if (!File.Exists(target) || new FileInfo(target).Length != payload.Length)
                        {
                            File.WriteAllBytes(target, payload);
                            log.AppendLine(module + ": extracted embedded native to " + target);
                        }
                        else
                        {
                            log.AppendLine(module + ": embedded native already extracted at " + target);
                        }

                        if (LoadLibraryW(target) != IntPtr.Zero)
                        {
                            SetDllDirectoryW(targetDir);
                            log.AppendLine(module + ": loaded extracted native from " + target);
                            return;
                        }

                        log.AppendLine(module + ": LoadLibrary failed (Win32 error " + Marshal.GetLastWin32Error() + ") for extracted " + target);
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine(module + ": extraction to " + targetDir + " failed: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                log.AppendLine(module + ": embedded extraction failed: " + ex.Message);
            }
        }

        private static void StageBesideManagedAssembly(string managedSimpleName, string nativeFile, StringBuilder log)
        {
            try
            {
                if (GetModuleHandleW(nativeFile) != IntPtr.Zero)
                {
                    return;
                }

                Assembly managed = FindOrLoadManaged(managedSimpleName);
                if (managed == null)
                {
                    log.AppendLine(nativeFile + ": managed '" + managedSimpleName + "' not loadable; skip staging.");
                    return;
                }

                string managedDir = Path.GetDirectoryName(managed.Location);
                log.AppendLine(managedSimpleName + " managed loaded from: " + managed.Location);

                if (string.IsNullOrEmpty(managedDir))
                {
                    return;
                }

                string target = Path.Combine(managedDir, nativeFile);
                if (File.Exists(target))
                {
                    log.AppendLine(nativeFile + ": already present beside managed assembly.");
                    return;
                }

                string rid = Environment.Is64BitProcess ? "win-x64" : "win-x86";
                string deployDir = SafeCodeBaseDirectory() ?? SafeLocationDirectory();
                string source = Combine(deployDir, Path.Combine("runtimes", Path.Combine(rid, Path.Combine("native", nativeFile))));

                if (!File.Exists(source))
                {
                    log.AppendLine(nativeFile + ": source not found for staging: " + source);
                    return;
                }

                File.Copy(source, target, false);
                log.AppendLine(nativeFile + ": staged beside managed assembly at " + target);
            }
            catch (Exception ex)
            {
                log.AppendLine(nativeFile + ": staging beside managed assembly failed: " + ex.Message);
            }
        }

        private static Assembly FindOrLoadManaged(string simpleName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                    {
                        return assembly;
                    }
                }
                catch
                {
                    // Dynamic assemblies etc. — ignore.
                }
            }

            try
            {
                // Not loaded yet: force-load the MANAGED assembly (safe — does not touch the
                // native type initializers). Resolution may be served by the host or by our
                // ResolveFromDeployFolder fallback.
                return Assembly.Load(simpleName);
            }
            catch
            {
                return null;
            }
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
