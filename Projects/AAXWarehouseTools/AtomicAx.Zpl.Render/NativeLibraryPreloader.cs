using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace AtomicAx.Zpl.Render
{
    /// <summary>
    /// Pre-loads the win-x64 native libraries (libSkiaSharp.dll and libHarfBuzzSharp.dll) before any
    /// SkiaSharp call. IIS shadow-copies managed assemblies, so SkiaSharp probes for its native
    /// library in the shadow-copy folder where it is absent; this class resolves the natives from the
    /// original deployment folder (Assembly.CodeBase) or from the embedded resources and loads them
    /// explicitly so later DllImport binds succeed. A module already loaded by another model is left
    /// as is.
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

        /// <summary>
        /// Loads a native module into the process.
        /// </summary>
        /// <param name="lpFileName">The path of the module to load.</param>
        /// <returns>The module handle, or zero on failure.</returns>
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        /// <summary>
        /// Gets the handle of a module already loaded in the process.
        /// </summary>
        /// <param name="lpModuleName">The base name of the module.</param>
        /// <returns>The module handle, or zero when the module is not loaded.</returns>
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        /// <summary>
        /// Adds a directory to the native library search path of the process.
        /// </summary>
        /// <param name="lpPathName">The directory to add.</param>
        /// <returns>True on success; otherwise false.</returns>
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectoryW(string lpPathName);

        /// <summary>
        /// Loads the native libraries once and records the probe log. Safe to call from every public
        /// entry point.
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

                // Serve assemblies from the deployment folder only when the host bind fails
                AppDomain.CurrentDomain.AssemblyResolve += ResolveFromDeployFolder;
                log.AppendLine("AssemblyResolve fallback registered for the deployment folder.");

                log.AppendLine("Process bitness: " + (Environment.Is64BitProcess ? "x64" : "x86"));
                log.AppendLine("BaseDirectory: " + SafeString(() => AppDomain.CurrentDomain.BaseDirectory));
                log.AppendLine("Assembly.Location dir: " + SafeLocationDirectory());
                log.AppendLine("Assembly.CodeBase dir: " + SafeCodeBaseDirectory());

                // Add the deployment folder to the native search path for SkiaSharp's own probing
                string deployDir = SafeCodeBaseDirectory() ?? SafeLocationDirectory();
                if (!string.IsNullOrEmpty(deployDir))
                {
                    bool setOk = SetDllDirectoryW(deployDir);
                    log.AppendLine("SetDllDirectory(" + deployDir + "): " + (setOk ? "OK" : ("FAILED Win32 " + Marshal.GetLastWin32Error())));
                }

                foreach (string module in NativeModules)
                {
                    LoadModule(module, log);

                    // Extract and load the embedded native when nothing is in-process yet
                    if (GetModuleHandleW(module) == IntPtr.Zero)
                    {
                        ExtractAndLoadEmbedded(module, log);
                    }
                }

                // Stage the native beside an unmerged managed SkiaSharp assembly
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
        /// Gets the probe and load log used in error messages and support diagnostics.
        /// </summary>
        public static string Diagnostics
        {
            get { return diagnostics; }
        }

        /// <summary>
        /// Loads a native module from the first candidate path where it exists, unless it is already
        /// loaded in the process.
        /// </summary>
        /// <param name="module">The file name of the native module.</param>
        /// <param name="log">The log that receives the probe results.</param>
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

        /// <summary>
        /// Builds the ordered list of paths where a native module may be found.
        /// </summary>
        /// <param name="module">The file name of the native module.</param>
        /// <returns>The candidate paths in probe order.</returns>
        private static string[] CandidatePaths(string module)
        {
            string codeBaseDir = SafeCodeBaseDirectory();
            string locationDir = SafeLocationDirectory();

            // Pick the runtimes folder matching the process bitness
            string rid = Environment.Is64BitProcess ? "win-x64" : "win-x86";
            string nativeSubPath = Path.Combine("runtimes", Path.Combine(rid, Path.Combine("native", module)));

            return new[]
            {
                // The original deployment folder, which survives IIS shadow copy
                Combine(codeBaseDir, nativeSubPath),
                // The shadow-copy folder under IIS or the real folder elsewhere
                Combine(locationDir, nativeSubPath),
                // Legacy flat layouts
                Combine(codeBaseDir, module),
                Combine(locationDir, module),
                Combine(AppDomain.CurrentDomain.BaseDirectory, module)
            };
        }

        /// <summary>
        /// Combines a directory and a file name, returning the file name alone when the directory is empty.
        /// </summary>
        /// <param name="directory">The directory, or null.</param>
        /// <param name="file">The file name or relative path.</param>
        /// <returns>The combined path.</returns>
        private static string Combine(string directory, string file)
        {
            return string.IsNullOrEmpty(directory) ? file : Path.Combine(directory, file);
        }

        /// <summary>
        /// Evaluates a string getter and returns its error message instead of throwing.
        /// </summary>
        /// <param name="getter">The getter to evaluate.</param>
        /// <returns>The value, or an error placeholder when the getter throws.</returns>
        private static string SafeString(Func<string> getter)
        {
            try { return getter(); }
            catch (Exception ex) { return "<error: " + ex.Message + ">"; }
        }

        /// <summary>
        /// Extracts the embedded native resource AAX.Natives.&lt;rid&gt;.&lt;module&gt; to the first writable
        /// location beside the assembly or under the temporary folder, and loads it.
        /// </summary>
        /// <param name="module">The file name of the native module.</param>
        /// <param name="log">The log that receives the extraction results.</param>
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

        /// <summary>
        /// Copies a native module from the deployment folder to the folder of its managed assembly so
        /// the managed loader can find it. Does nothing when the module is already loaded or present.
        /// </summary>
        /// <param name="managedSimpleName">The simple name of the managed assembly.</param>
        /// <param name="nativeFile">The file name of the native module.</param>
        /// <param name="log">The log that receives the staging results.</param>
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

        /// <summary>
        /// Finds a managed assembly in the current application domain, loading it when it is not
        /// loaded yet.
        /// </summary>
        /// <param name="simpleName">The simple name of the managed assembly.</param>
        /// <returns>The assembly, or null when it cannot be loaded.</returns>
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
                    // Ignore dynamic assemblies
                }
            }

            try
            {
                // Load the managed assembly without touching the native type initializers
                return Assembly.Load(simpleName);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves an assembly the host failed to bind by loading it from the deployment folder.
        /// </summary>
        /// <param name="sender">The application domain raising the event.</param>
        /// <param name="args">The resolve arguments naming the requested assembly.</param>
        /// <returns>The loaded assembly, or null when it is not in the deployment folder.</returns>
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

        /// <summary>
        /// Gets the original deployment folder of this assembly from its code base.
        /// </summary>
        /// <returns>The folder, or null when it cannot be determined.</returns>
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

        /// <summary>
        /// Gets the folder this assembly was loaded from.
        /// </summary>
        /// <returns>The folder, or null when it cannot be determined.</returns>
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
