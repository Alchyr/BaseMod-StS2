namespace BaseLib.HotReload;

/// <summary>
/// Watches the game's mods directory for new or changed DLL files and triggers
/// hot reload automatically. Only reacts to DLLs matching the hot-reload stamped
/// naming pattern (e.g., MyMod_hr143052789.dll) to avoid triggering on unrelated
/// file changes.
///
/// Uses .NET FileSystemWatcher as the primary mechanism, with an optional polling
/// fallback for platforms where FSW is unreliable (notably Linux with inotify limits).
/// MSBuild writes files in multiple passes, so we debounce before triggering.
/// </summary>
public sealed class ModFileWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private Timer? _pollTimer;
    private readonly string _modsDirectory;
    private readonly int _debounceMs;
    private readonly int _pollIntervalMs;
    private CancellationTokenSource? _debounceCts;

    // Track the last DLL we triggered on and when, to avoid double-fires
    private string? _lastTriggeredPath;
    private DateTime _lastTriggeredTime;

    // For polling: track the newest DLL write time we've seen
    private DateTime _lastKnownWriteTime = DateTime.MinValue;

    public bool IsWatching { get; private set; }

    /// <summary>
    /// Fired when a hot-reload-stamped DLL is detected. The string parameter
    /// is the absolute path to the DLL. Callers should dispatch to the main
    /// thread before calling HotReloadEngine.Reload().
    /// </summary>
    public event Action<string>? OnModDllChanged;

    /// <param name="modsDirectory">The game's mods/ directory to watch.</param>
    /// <param name="debounceMs">How long to wait after a file event before triggering (default 500ms).</param>
    /// <param name="pollIntervalMs">Polling fallback interval. 0 disables polling (default 2000ms).</param>
    public ModFileWatcher(string modsDirectory, int debounceMs = 500, int pollIntervalMs = 2000)
    {
        _modsDirectory = modsDirectory;
        _debounceMs = debounceMs;
        _pollIntervalMs = pollIntervalMs;
    }

    public void Start()
    {
        if (IsWatching) return;

        // Primary: FileSystemWatcher (efficient, event-driven)
        try
        {
            _watcher = new FileSystemWatcher(_modsDirectory)
            {
                Filter = "*.dll",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            // FileSystemWatcher can fail on some platforms (e.g., network drives, certain Linux configs).
            // We'll fall back to polling below.
            BaseLibMain.Logger.Warn($"[HotReload] FileSystemWatcher failed, using polling only: {ex.Message}");
        }

        // Fallback: periodic polling (reliable everywhere, catches events FSW might miss)
        if (_pollIntervalMs > 0)
        {
            // Snapshot current state so we don't trigger on DLLs that existed before we started
            _lastKnownWriteTime = GetNewestStampedDllWriteTime();
            _pollTimer = new Timer(PollForChanges, null, _pollIntervalMs, _pollIntervalMs);
        }

        IsWatching = true;
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        _pollTimer?.Dispose();
        _pollTimer = null;
        _debounceCts?.Cancel();
        IsWatching = false;
    }

    /// <summary>
    /// Handle a FileSystemWatcher event. We debounce because MSBuild writes the DLL
    /// in multiple passes and FSW fires for each one.
    /// </summary>
    private async void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Only trigger for hot-reload-stamped DLLs (e.g., MyMod_hr143052789.dll).
        // This avoids reacting to dependency DLLs, manifest copies, etc.
        if (!AssemblyStamper.IsHotReloadStamped(e.FullPath))
            return;

        // Deduplicate rapid events for the same file (Changed + Created fire together)
        var now = DateTime.UtcNow;
        if (_lastTriggeredPath == e.FullPath && (now - _lastTriggeredTime).TotalMilliseconds < _debounceMs * 2)
            return;

        // Cancel any pending debounce timer and start a new one
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        try
        {
            // Wait for the build to finish writing
            await Task.Delay(_debounceMs, token);

            _lastTriggeredPath = e.FullPath;
            _lastTriggeredTime = DateTime.UtcNow;
            _lastKnownWriteTime = File.GetLastWriteTimeUtc(e.FullPath);
            OnModDllChanged?.Invoke(e.FullPath);
        }
        catch (OperationCanceledException)
        {
            // A newer event came in — this debounce was superseded, which is fine
        }
    }

    /// <summary>
    /// Polling fallback: scan the mods directory for stamped DLLs newer than our last
    /// known write time. This catches events that FileSystemWatcher might miss
    /// (Linux inotify limits, network drives, etc.).
    /// </summary>
    private void PollForChanges(object? state)
    {
        try
        {
            var (newestPath, newestTime) = FindNewestStampedDll();
            if (newestPath == null || newestTime <= _lastKnownWriteTime) return;

            // Deduplicate against FSW-triggered events
            var now = DateTime.UtcNow;
            if (_lastTriggeredPath == newestPath && (now - _lastTriggeredTime).TotalMilliseconds < _pollIntervalMs * 2)
                return;

            _lastTriggeredPath = newestPath;
            _lastTriggeredTime = now;
            _lastKnownWriteTime = newestTime;
            OnModDllChanged?.Invoke(newestPath);
        }
        catch
        {
            // Polling errors are silently ignored — the next poll will try again
        }
    }

    private DateTime GetNewestStampedDllWriteTime()
    {
        var (_, time) = FindNewestStampedDll();
        return time;
    }

    private (string? path, DateTime writeTime) FindNewestStampedDll()
    {
        string? newestPath = null;
        var newestTime = DateTime.MinValue;

        try
        {
            foreach (var dll in Directory.EnumerateFiles(_modsDirectory, "*.dll", SearchOption.AllDirectories))
            {
                if (!AssemblyStamper.IsHotReloadStamped(dll)) continue;
                var writeTime = File.GetLastWriteTimeUtc(dll);
                if (writeTime > newestTime)
                {
                    newestTime = writeTime;
                    newestPath = dll;
                }
            }
        }
        catch
        {
            // Directory might be inaccessible momentarily
        }

        return (newestPath, newestTime);
    }

    public void Dispose()
    {
        Stop();
        _debounceCts?.Dispose();
    }
}
