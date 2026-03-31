using System.Reflection;
using BaseLib.Config;

namespace BaseLib.HotReload;

/// <summary>
/// Public API for hot-reloading mod assemblies at runtime.
/// No external dependencies — pure C# + Harmony + GodotSharp.
///
/// Usage:
///   HotReloadEngine.Reload("E:/game/mods/mymod/MyMod_hr143052789.dll");
///   HotReloadEngine.ReloadByModId("MyMod");
///
/// Or use the in-game console command: hotreload [dll_path_or_mod_id]
/// </summary>
public static class HotReloadEngine
{
    // Only one reload at a time — we lock to prevent concurrent reloads
    // (e.g., file watcher triggering while a manual reload is in progress)
    private static readonly object _hotReloadLock = new();

    // Per-mod session tracking — survives across multiple reloads of the same mod
    private static readonly Dictionary<string, HotReloadSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    // History of recent reloads for diagnostics
    private static readonly List<HotReloadResult> _history = [];
    private const int MaxHistory = 20;

    // The file watcher that auto-triggers reload when a DLL changes
    private static ModFileWatcher? _fileWatcher;

    /// <summary>
    /// Current reload step name, or empty if no reload is in progress.
    /// Check this to show progress in UI or respond to queries.
    /// </summary>
    public static string CurrentProgress { get; internal set; } = "";

    /// <summary>
    /// History of the most recent hot reloads (up to 20).
    /// </summary>
    public static IReadOnlyList<HotReloadResult> ReloadHistory => _history;

    /// <summary>
    /// The active file watcher, or null if disabled.
    /// </summary>
    public static ModFileWatcher? FileWatcher => _fileWatcher;

    /// <summary>
    /// Fired after every reload attempt (success or failure).
    /// Subscribe to this to perform custom cleanup or refresh logic.
    /// </summary>
    public static event Action<HotReloadResult>? OnReloadComplete;

    /// <summary>
    /// Reload a mod from a DLL path. Thread-safe (serialized via lock).
    /// Should be called from the main thread (Godot scene tree access in Step 12).
    /// </summary>
    /// <param name="dllPath">Absolute path to the mod DLL.</param>
    /// <param name="tier">1 = patch-only, 2 = entities + patches + loc, 3 = full + PCK.</param>
    /// <param name="pckPath">Path to PCK file for tier 3 reloads.</param>
    public static HotReloadResult Reload(string dllPath, int tier = 2, string? pckPath = null)
    {
        if (!Monitor.TryEnter(_hotReloadLock))
        {
            return new HotReloadResult
            {
                Success = false,
                Errors = ["Hot reload already in progress. Wait for the current reload to finish."],
            };
        }

        try
        {
            string modKey = AssemblyStamper.NormalizeModKey(dllPath);
            CurrentProgress = "starting";

            var session = GetOrCreateSession(modKey);
            var result = HotReloadPipeline.Execute(dllPath, tier, pckPath, session);

            // Store in history
            lock (_history)
            {
                _history.Add(result);
                while (_history.Count > MaxHistory)
                    _history.RemoveAt(0);
            }

            // Notify listeners
            try { OnReloadComplete?.Invoke(result); }
            catch (Exception ex) { BaseLibMain.Logger.Error($"[HotReload] OnReloadComplete handler error: {ex}"); }

            return result;
        }
        finally
        {
            CurrentProgress = "";
            Monitor.Exit(_hotReloadLock);
        }
    }

    /// <summary>
    /// Scan the mods directory for the most recently modified DLL matching the given
    /// mod ID and reload it. Convenient when you don't have the exact DLL path.
    /// Pass tier=0 to auto-detect the tier from the mod directory contents.
    /// </summary>
    public static HotReloadResult ReloadByModId(string modId, int tier = 0)
    {
        // Look for the mod's folder in the game's mods directory
        var modsDir = FindModsDirectory();
        if (modsDir == null)
        {
            return new HotReloadResult
            {
                Success = false,
                Errors = [$"Could not find game mods directory"],
            };
        }

        var modDir = Path.Combine(modsDir, modId);
        if (!Directory.Exists(modDir))
        {
            return new HotReloadResult
            {
                Success = false,
                Errors = [$"Mod directory not found: {modDir}"],
            };
        }

        // Find the most recently modified DLL in the mod directory
        var latestDll = Directory.GetFiles(modDir, "*.dll")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latestDll == null)
        {
            return new HotReloadResult
            {
                Success = false,
                Errors = [$"No DLL found in {modDir}"],
            };
        }

        // Auto-detect tier if not specified:
        //   - Has a .pck file → tier 3 (entities + patches + Godot resources)
        //   - Otherwise → tier 2 (entities + patches + localization)
        // Tier 1 (patch-only) must be explicitly requested since it's rare.
        if (tier <= 0)
            tier = Directory.GetFiles(modDir, "*.pck").Length > 0 ? 3 : 2;

        string? pckPath = null;
        if (tier >= 3)
            pckPath = Directory.GetFiles(modDir, "*.pck").FirstOrDefault();

        return Reload(latestDll, tier, pckPath);
    }

    /// <summary>
    /// Initialize the hot reload subsystem. Called from BaseLibMain.Initialize().
    /// Sets up the file watcher if enabled in config.
    /// </summary>
    internal static void Init()
    {
        // Run startup self-tests to verify all reflection targets exist in this game version.
        // If any fail, something in the game changed and the pipeline will break at runtime.
        HotReloadSelfTests.RunStartupTests();

        // Register assembly resolution handlers early (at game startup) so they're in place
        // before any hot reload happens. The MCP bridge does this in its ModEntry.Init().
        // Two handlers are needed:
        //   AppDomain.AssemblyResolve — fires for version mismatches
        //   ALC.Resolving — fires when default probing fails
        // Both redirect by assembly short name to already-loaded assemblies.
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var requestedName = new System.Reflection.AssemblyName(args.Name);
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, requestedName.Name, StringComparison.Ordinal));
        };
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += TypeSignatureHasher.DefaultAlcResolving;
        HotReloadPipeline.MarkResolversRegistered();

        BaseLibMain.Logger.Info("[HotReload] Hot reload engine initialized");

        if (BaseLibConfig.EnableFileWatcher)
        {
            var modsDir = FindModsDirectory();
            if (modsDir != null)
            {
                _fileWatcher = new ModFileWatcher(modsDir);
                _fileWatcher.OnModDllChanged += OnWatcherDetectedChange;
                _fileWatcher.Start();
                BaseLibMain.Logger.Info($"[HotReload] File watcher started on {modsDir}");
            }
            else
            {
                BaseLibMain.Logger.Warn("[HotReload] File watcher enabled but could not find mods directory");
            }
        }
    }

    // ─── Private helpers ────────────────────────────────────────────────

    private static HotReloadSession GetOrCreateSession(string modKey)
    {
        lock (_sessions)
        {
            if (!_sessions.TryGetValue(modKey, out var session))
            {
                session = new HotReloadSession(modKey);
                _sessions[modKey] = session;
            }
            return session;
        }
    }

    /// <summary>
    /// Called by the file watcher when a DLL changes. Dispatches to main thread
    /// since the reload pipeline needs Godot scene tree access.
    /// </summary>
    private static void OnWatcherDetectedChange(string dllPath)
    {
        BaseLibMain.Logger.Info($"[HotReload] File watcher detected change: {dllPath}");

        // The file watcher fires on a background thread, but the reload pipeline needs
        // Godot's main thread (scene tree access, node property writes). CallDeferred
        // queues the lambda to run on the next idle frame.
        Godot.Callable.From(() =>
        {
            var result = Reload(dllPath);
            BaseLibMain.Logger.Info($"[HotReload] Auto-reload result: {result.Summary}");
        }).CallDeferred();
    }

    /// <summary>
    /// Try to find the game's mods directory. Returns null if not found.
    /// </summary>
    private static string? FindModsDirectory()
    {
        // The game executable is typically at {GameDir}/SlayTheSpire2.exe
        // and mods are at {GameDir}/mods/
        var executable = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (executable != null)
        {
            var gameDir = Path.GetDirectoryName(executable);
            if (gameDir != null)
            {
                var modsDir = Path.Combine(gameDir, "mods");
                if (Directory.Exists(modsDir))
                    return modsDir;
            }
        }

        return null;
    }
}
