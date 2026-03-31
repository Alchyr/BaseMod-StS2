using System.Collections.Concurrent;
using System.Diagnostics;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;

namespace BaseLib.Utils;

/// <summary>
/// Provides direct access to the FMOD audio engine for playing custom sounds.
/// Wraps the FmodServer GDExtension singleton so mods don't have to deal with
/// untyped Call() chains everywhere.
///
/// For existing game sounds, just pass the event path:
///   FmodAudio.PlayEvent("event:/sfx/heal");
///
/// For custom audio files (wav, ogg, mp3, etc.):
///   FmodAudio.PlayFile("path/to/sound.wav");
///
/// For full FMOD Studio banks built against the game's FMOD 2.03.x runtime:
///   FmodAudio.LoadBank("path/to/custom.bank");
///   FmodAudio.PlayEvent("event:/mods/mymod/my_sound");
/// </summary>
[Obsolete("This class is not guaranteed to continue to exist in its current state, and is not recommended for use.")]
public static class FmodAudio
{
    private static GodotObject? _server;
    private static readonly Dictionary<string, Variant> _loadedFiles = new();
    private static readonly Dictionary<string, Variant> _loadedBanks = new();

    // Replacement registry: original event path -> replacement action
    private static readonly Dictionary<string, Func<string, float, bool>> _replacements = new();
    private static bool _replacementPatchApplied;

    // Cooldown tracking: event path -> last play timestamp (ticks)
    private static readonly ConcurrentDictionary<string, long> _cooldowns = new();

    // Sound pools: pool name -> list of paths/events
    private static readonly Dictionary<string, SoundPool> _soundPools = new();
    private static readonly Random _poolRng = new();

    private static GodotObject? Server
    {
        get
        {
            if (_server != null) return _server;
            try
            {
                _server = Engine.GetSingleton("FmodServer");
            }
            catch (Exception ex)
            {
                BaseLibMain.Logger.Error($"Failed to get FmodServer singleton: {ex.Message}");
            }
            return _server;
        }
    }

    // ── Playing events ──────────────────────────────────────────────────
    
    /*
     * In combat: Use SfxCmd.Play to play an event path,
     * or NAudioManager.Instance.PlayOneShot.
     */

    /// <summary>
    /// Fire-and-forget play of an FMOD event. Works with any event path
    /// from loaded banks (the game's built-in banks or your own).
    /// </summary>
    public static bool PlayEvent(string eventPath)
    {
        if (Server == null) return false;
        try
        {
            Server.Call("play_one_shot", eventPath);
            return true;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.PlayEvent failed for '{eventPath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Play an FMOD event with parameters (e.g. "EnemyImpact_Intensity" for damage sounds).
    /// </summary>
    public static bool PlayEvent(string eventPath, Dictionary<string, float> parameters)
    {
        if (Server == null) return false;
        try
        {
            var dict = new Godot.Collections.Dictionary();
            foreach (var kv in parameters)
                dict[kv.Key] = kv.Value;
            Server.Call("play_one_shot_with_params", eventPath, dict);
            return true;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.PlayEvent failed for '{eventPath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Play an FMOD event with cooldown. If the same event was played within
    /// cooldownMs, the call is silently skipped. Useful for sounds triggered
    /// by rapid game events (e.g. a power that fires on every card play).
    /// </summary>
    public static bool PlayEvent(string eventPath, int cooldownMs)
    {
        var now = Stopwatch.GetTimestamp();
        var cooldownTicks = (long)(cooldownMs * Stopwatch.Frequency / 1000.0);

        if (_cooldowns.TryGetValue(eventPath, out var lastPlay))
        {
            if (now - lastPlay < cooldownTicks) return false;
        }

        _cooldowns[eventPath] = now;
        return PlayEvent(eventPath);
    }

    /// <summary>
    /// Play an event by its GUID instead of path. Slightly faster lookup
    /// and immune to event renames in FMOD Studio.
    /// </summary>
    public static bool PlayEventByGuid(string guid)
    {
        if (Server == null) return false;
        try
        {
            Server.Call("play_one_shot_using_guid", guid);
            return true;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.PlayEventByGuid failed for '{guid}': {ex.Message}");
            return false;
        }
    }

    // ── Event instances (for sounds you need to control) ────────────────

    /// <summary>
    /// Create an event instance you can start/stop/adjust yourself.
    /// Don't forget to call Release() on the returned object when you're done.
    /// </summary>
    public static GodotObject? CreateEventInstance(string eventPath)
    {
        if (Server == null) return null;
        try
        {
            return Server.Call("create_event_instance", eventPath).Obj as GodotObject;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.CreateEventInstance failed for '{eventPath}': {ex.Message}");
            return null;
        }
    }

    // ── Custom file playback ────────────────────────────────────────────

    /// <summary>
    /// Play a sound file (wav, ogg, mp3) through FMOD. The file gets loaded
    /// into FMOD's memory on first use and stays cached for subsequent calls.
    ///
    /// Returns the FmodSound handle so you can control it (stop, volume, pitch).
    /// The handle has: play(), stop(), set_volume(float), set_pitch(float),
    /// is_playing(), set_paused(bool), release().
    /// </summary>
    public static GodotObject? PlayFile(string absolutePath, float volume = 1.0f, float pitch = 1.0f)
    {
        var sound = CreateSoundInstance(absolutePath);
        if (sound == null) return null;

        try
        {
            if (volume != 1.0f) sound.Call("set_volume", volume);
            if (pitch != 1.0f) sound.Call("set_pitch", pitch);
            sound.Call("play");
            return sound;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.PlayFile failed for '{absolutePath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Play a custom file with cooldown, same idea as PlayEvent with cooldown.
    /// </summary>
    public static GodotObject? PlayFile(string absolutePath, int cooldownMs, float volume = 1.0f, float pitch = 1.0f)
    {
        var now = Stopwatch.GetTimestamp();
        var cooldownTicks = (long)(cooldownMs * Stopwatch.Frequency / 1000.0);

        if (_cooldowns.TryGetValue(absolutePath, out var lastPlay))
        {
            if (now - lastPlay < cooldownTicks) return null;
        }

        _cooldowns[absolutePath] = now;
        return PlayFile(absolutePath, volume, pitch);
    }

    /// <summary>
    /// Load a sound file into FMOD without playing it. Useful if you want to
    /// preload during init so there's no hitch on first play.
    /// </summary>
    public static bool PreloadFile(string absolutePath)
    {
        if (Server == null) return false;
        if (_loadedFiles.ContainsKey(absolutePath)) return true;

        try
        {
            var result = Server.Call("load_file_as_sound", absolutePath);
            _loadedFiles[absolutePath] = result;
            return true;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.PreloadFile failed for '{absolutePath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Load a music/long-form file as streaming (reads from disk during playback
    /// instead of loading the whole thing into memory). Use this for tracks
    /// longer than ~10 seconds to save RAM.
    /// </summary>
    public static bool PreloadMusic(string absolutePath)
    {
        if (Server == null) return false;
        if (_loadedFiles.ContainsKey(absolutePath)) return true;

        try
        {
            var result = Server.Call("load_file_as_music", absolutePath);
            _loadedFiles[absolutePath] = result;
            return true;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.PreloadMusic failed for '{absolutePath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Play a long-form audio file using streaming (doesn't load entire file
    /// into memory). Ideal for custom music tracks, long ambient loops, etc.
    /// </summary>
    public static GodotObject? PlayMusic(string absolutePath, float volume = 1.0f, float pitch = 1.0f)
    {
        // Make sure it's loaded as streaming
        if (!_loadedFiles.ContainsKey(absolutePath) && !PreloadMusic(absolutePath))
            return null;

        var sound = CreateSoundInstance(absolutePath);
        if (sound == null) return null;

        try
        {
            if (volume != 1.0f) sound.Call("set_volume", volume);
            if (pitch != 1.0f) sound.Call("set_pitch", pitch);
            sound.Call("play");
            return sound;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.PlayMusic failed for '{absolutePath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get a raw FmodSound instance for manual control. Loads the file first
    /// if it hasn't been loaded yet.
    /// </summary>
    public static GodotObject? CreateSoundInstance(string absolutePath)
    {
        if (Server == null) return null;

        // make sure it's loaded
        if (!_loadedFiles.ContainsKey(absolutePath) && !PreloadFile(absolutePath))
            return null;

        try
        {
            return Server.Call("create_sound_instance", absolutePath).Obj as GodotObject;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.CreateSoundInstance failed for '{absolutePath}': {ex.Message}");
            return null;
        }
    }

    public static void UnloadFile(string absolutePath)
    {
        if (Server == null) return;
        try
        {
            Server.Call("unload_file", absolutePath);
            _loadedFiles.Remove(absolutePath);
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.UnloadFile failed for '{absolutePath}': {ex.Message}");
        }
    }

    // ── Bank loading ────────────────────────────────────────────────────

    /// <summary>
    /// Load a custom FMOD bank. Must be built with FMOD Studio 2.03.x to match
    /// the game's runtime. Load your strings bank first, then content banks.
    /// </summary>
    public static bool LoadBank(string bankPath)
    {
        if (Server == null) return false;
        try
        {
            var result = Server.Call("load_bank", bankPath, 0);
            _loadedBanks[bankPath] = result;
            BaseLibMain.Logger.Info($"Loaded FMOD bank: {bankPath}");
            return true;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.LoadBank failed for '{bankPath}': {ex.Message}");
            return false;
        }
    }

    public static void UnloadBank(string bankPath)
    {
        if (Server == null) return;
        try
        {
            Server.Call("unload_bank", bankPath);
            _loadedBanks.Remove(bankPath);
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.UnloadBank failed for '{bankPath}': {ex.Message}");
        }
    }

    // ── Sound replacement registry ──────────────────────────────────────

    /// <summary>
    /// Register a replacement for a game sound. When the game tries to play
    /// originalEvent, your replacement runs instead. Lets multiple mods swap
    /// sounds without writing their own Harmony patches.
    ///
    /// The handler receives (eventPath, volume) and returns true if it handled
    /// playback (which skips the original), or false to let it through.
    /// </summary>
    public static void RegisterReplacement(string originalEvent, Func<string, float, bool> handler)
    {
        _replacements[originalEvent] = handler;
        EnsureReplacementPatch();
    }

    /// <summary>
    /// Shorthand: replace an FMOD event with a custom file.
    /// </summary>
    public static void RegisterFileReplacement(string originalEvent, string replacementFilePath)
    {
        RegisterReplacement(originalEvent, (_, volume) =>
        {
            PlayFile(replacementFilePath, volume);
            return true;
        });
    }

    /// <summary>
    /// Shorthand: replace an FMOD event with a different FMOD event.
    /// </summary>
    public static void RegisterEventReplacement(string originalEvent, string replacementEvent)
    {
        RegisterReplacement(originalEvent, (_, _) =>
        {
            PlayEvent(replacementEvent);
            return true;
        });
    }

    /// <summary>
    /// Remove a previously registered replacement.
    /// </summary>
    public static void RemoveReplacement(string originalEvent)
    {
        _replacements.Remove(originalEvent);
    }

    /// <summary>
    /// Remove all replacements registered by any mod.
    /// </summary>
    public static void ClearReplacements()
    {
        _replacements.Clear();
    }

    private static void EnsureReplacementPatch()
    {
        if (_replacementPatchApplied) return;
        _replacementPatchApplied = true;

        var harmony = new Harmony("BaseLib.FmodAudio.Replacements");
        var original = AccessTools.Method(typeof(NAudioManager), nameof(NAudioManager.PlayOneShot),
            [typeof(string), typeof(float)]);
        var prefix = AccessTools.Method(typeof(FmodAudio), nameof(ReplacementPrefix));
        if (original != null && prefix != null)
        {
            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        }
    }

    private static bool ReplacementPrefix(string path, float volume)
    {
        if (_replacements.TryGetValue(path, out var handler))
        {
            try
            {
                if (handler(path, volume)) return false; // skip original
            }
            catch (Exception ex)
            {
                BaseLibMain.Logger.Error($"FmodAudio replacement handler for '{path}' threw: {ex.Message}");
            }
        }
        return true; // let original through
    }

    // ── Sound pools (random selection) ──────────────────────────────────

    /// <summary>
    /// Create a named pool of sounds. When played, one is chosen at random.
    /// Avoids repeating the same sound twice in a row.
    ///
    /// Entries can be FMOD event paths ("event:/...") or absolute file paths.
    /// </summary>
    public static void CreatePool(string poolName, params string[] soundPaths)
    {
        _soundPools[poolName] = new SoundPool(soundPaths);
    }

    /// <summary>
    /// Add more sounds to an existing pool, or create it if it doesn't exist.
    /// </summary>
    public static void AddToPool(string poolName, params string[] soundPaths)
    {
        if (!_soundPools.TryGetValue(poolName, out var pool))
        {
            pool = new SoundPool([]);
            _soundPools[poolName] = pool;
        }
        pool.Entries.AddRange(soundPaths);
    }

    /// <summary>
    /// Play a random sound from a named pool. Returns null if the pool doesn't exist
    /// or is empty. For file entries, returns the FmodSound handle.
    /// </summary>
    public static GodotObject? PlayPool(string poolName, float volume = 1.0f, float pitch = 1.0f)
    {
        if (!_soundPools.TryGetValue(poolName, out var pool) || pool.Entries.Count == 0)
            return null;

        var entry = pool.PickNext(_poolRng);
        if (entry.StartsWith("event:/"))
        {
            PlayEvent(entry);
            return null; // fire-and-forget events don't return handles
        }
        return PlayFile(entry, volume, pitch);
    }

    /// <summary>
    /// Play from a pool with cooldown. Skipped if the pool was played within cooldownMs.
    /// </summary>
    public static GodotObject? PlayPool(string poolName, int cooldownMs, float volume = 1.0f, float pitch = 1.0f)
    {
        var key = $"__pool__{poolName}";
        var now = Stopwatch.GetTimestamp();
        var cooldownTicks = (long)(cooldownMs * Stopwatch.Frequency / 1000.0);

        if (_cooldowns.TryGetValue(key, out var lastPlay))
        {
            if (now - lastPlay < cooldownTicks) return null;
        }

        _cooldowns[key] = now;
        return PlayPool(poolName, volume, pitch);
    }

    public static void RemovePool(string poolName)
    {
        _soundPools.Remove(poolName);
    }

    private class SoundPool(string[] entries)
    {
        public readonly List<string> Entries = [.. entries];
        private int _lastIndex = -1;

        public string PickNext(Random rng)
        {
            if (Entries.Count == 1) return Entries[0];

            // Avoid repeating the same sound back-to-back
            int index;
            do { index = rng.Next(Entries.Count); }
            while (index == _lastIndex && Entries.Count > 1);

            _lastIndex = index;
            return Entries[index];
        }
    }

    // ── Snapshots ───────────────────────────────────────────────────────

    /// <summary>
    /// Start an FMOD snapshot (e.g. "snapshot:/pause" for the game's pause
    /// ducking effect). Snapshots apply mixer effects globally — low-pass
    /// filters, reverb changes, volume ducking, etc.
    ///
    /// Returns the snapshot instance. Call stop + release on it when done.
    /// </summary>
    public static GodotObject? StartSnapshot(string snapshotPath)
    {
        if (Server == null) return null;
        try
        {
            var instance = Server.Call("create_event_instance", snapshotPath).Obj as GodotObject;
            instance?.Call("start");
            return instance;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.StartSnapshot failed for '{snapshotPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Stop a snapshot instance. allowFadeout=true lets FMOD's release envelope
    /// run (smooth transition), false cuts immediately.
    /// </summary>
    public static void StopSnapshot(GodotObject? snapshot, bool allowFadeout = true)
    {
        if (snapshot == null) return;
        try
        {
            snapshot.Call("stop", allowFadeout ? 0 : 1);
            snapshot.Call("release");
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.StopSnapshot failed: {ex.Message}");
        }
    }

    // ── Bus control ─────────────────────────────────────────────────────

    /// <summary>
    /// Get a bus object for direct control. Common paths:
    ///   "bus:/master", "bus:/master/sfx", "bus:/master/music",
    ///   "bus:/master/ambience", "bus:/master/sfx/Reverb", "bus:/master/sfx/chorus"
    /// </summary>
    public static GodotObject? GetBus(string busPath)
    {
        if (Server == null) return null;
        try
        {
            return Server.Call("get_bus", busPath).Obj as GodotObject;
        }
        catch (Exception ex)
        {
            BaseLibMain.Logger.Error($"FmodAudio.GetBus failed for '{busPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Set the volume on a bus. Affects all sounds routed through it.
    /// </summary>
    public static void SetBusVolume(string busPath, float volume)
    {
        var bus = GetBus(busPath);
        bus?.Call("set_volume", volume);
    }

    /// <summary>
    /// Get the current volume of a bus.
    /// </summary>
    public static float GetBusVolume(string busPath)
    {
        var bus = GetBus(busPath);
        if (bus == null) return 0f;
        try { return bus.Call("get_volume").AsSingle(); }
        catch { return 0f; }
    }

    /// <summary>
    /// Mute or unmute a bus.
    /// </summary>
    public static void SetBusMute(string busPath, bool muted)
    {
        var bus = GetBus(busPath);
        bus?.Call("set_mute", muted);
    }

    /// <summary>
    /// Pause or unpause all sounds on a bus.
    /// </summary>
    public static void SetBusPaused(string busPath, bool paused)
    {
        var bus = GetBus(busPath);
        bus?.Call("set_paused", paused);
    }

    // ── Global parameters ───────────────────────────────────────────────

    public static void SetGlobalParameter(string name, float value)
    {
        if (Server == null) return;
        try { Server.Call("set_global_parameter_by_name", name, value); }
        catch (Exception ex) { BaseLibMain.Logger.Error($"FmodAudio.SetGlobalParameter({name}): {ex.Message}"); }
    }

    /// <summary>
    /// Set a global parameter using a label instead of a numeric value.
    /// Some parameters define named labels (e.g. Progress has "Enemy", "Merchant", etc.)
    /// </summary>
    public static void SetGlobalParameterByLabel(string name, string label)
    {
        if (Server == null) return;
        try { Server.Call("set_global_parameter_by_name_with_label", name, label); }
        catch (Exception ex) { BaseLibMain.Logger.Error($"FmodAudio.SetGlobalParameterByLabel({name}, {label}): {ex.Message}"); }
    }

    public static float GetGlobalParameter(string name)
    {
        if (Server == null) return 0f;
        try { return Server.Call("get_global_parameter_by_name", name).AsSingle(); }
        catch { return 0f; }
    }

    // ── Mute/pause all ──────────────────────────────────────────────────

    public static void MuteAll()
    {
        if (Server == null) return;
        try { Server.Call("mute_all_events"); }
        catch (Exception ex) { BaseLibMain.Logger.Error($"FmodAudio.MuteAll: {ex.Message}"); }
    }

    public static void UnmuteAll()
    {
        if (Server == null) return;
        try { Server.Call("unmute_all_events"); }
        catch (Exception ex) { BaseLibMain.Logger.Error($"FmodAudio.UnmuteAll: {ex.Message}"); }
    }

    public static void PauseAll()
    {
        if (Server == null) return;
        try { Server.Call("pause_all_events"); }
        catch (Exception ex) { BaseLibMain.Logger.Error($"FmodAudio.PauseAll: {ex.Message}"); }
    }

    public static void UnpauseAll()
    {
        if (Server == null) return;
        try { Server.Call("unpause_all_events"); }
        catch (Exception ex) { BaseLibMain.Logger.Error($"FmodAudio.UnpauseAll: {ex.Message}"); }
    }

    // ── DSP buffer tuning ───────────────────────────────────────────────

    /// <summary>
    /// Adjust the DSP buffer size. Larger buffers reduce crackling on slow
    /// hardware but increase audio latency. Default is usually 1024 samples
    /// with 4 buffers. Only change this if players report audio glitches.
    /// </summary>
    public static void SetDspBufferSize(int bufferLength, int numBuffers)
    {
        if (Server == null) return;
        try { Server.Call("set_system_dsp_buffer_size", bufferLength, numBuffers); }
        catch (Exception ex) { BaseLibMain.Logger.Error($"FmodAudio.SetDspBufferSize: {ex.Message}"); }
    }

    /// <summary>
    /// Get current DSP buffer settings: (bufferLength, numBuffers).
    /// </summary>
    public static (int bufferLength, int numBuffers) GetDspBufferSettings()
    {
        if (Server == null) return (0, 0);
        try
        {
            int length = Server.Call("get_system_dsp_buffer_length").AsInt32();
            int count = Server.Call("get_system_dsp_num_buffers").AsInt32();
            return (length, count);
        }
        catch { return (0, 0); }
    }

    // ── Performance monitoring ──────────────────────────────────────────

    /// <summary>
    /// Get FMOD performance data. Returns raw performance info from the engine —
    /// CPU usage, memory, channels, etc. Useful for debugging audio-heavy mods.
    /// </summary>
    public static Variant GetPerformanceData()
    {
        if (Server == null) return default;
        try { return Server.Call("get_performance_data"); }
        catch { return default; }
    }

    // ── Utilities ───────────────────────────────────────────────────────

    /// <summary>
    /// Check whether an event path exists in any currently loaded bank.
    /// </summary>
    public static bool EventExists(string eventPath)
    {
        if (Server == null) return false;
        try { return Server.Call("check_event_path", eventPath).AsBool(); }
        catch { return false; }
    }

    /// <summary>
    /// Check whether a bus path exists.
    /// </summary>
    public static bool BusExists(string busPath)
    {
        if (Server == null) return false;
        try { return Server.Call("check_bus_path", busPath).AsBool(); }
        catch { return false; }
    }

    /// <summary>
    /// Unload everything this helper has loaded and clear all registrations.
    /// FMOD cleans up on game exit anyway, but this is here for mods that
    /// need to tear down gracefully.
    /// </summary>
    public static void UnloadAll()
    {
        foreach (var path in _loadedFiles.Keys.ToList())
            UnloadFile(path);
        foreach (var path in _loadedBanks.Keys.ToList())
            UnloadBank(path);
        _replacements.Clear();
        _cooldowns.Clear();
        _soundPools.Clear();
    }
}
