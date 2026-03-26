# BaseLib: FmodAudio — Custom Audio for Mods

STS2 uses FMOD Studio for all audio. BaseLib's `FmodAudio` utility gives mods direct access to the FMOD engine so you can play custom sounds, load your own audio files, and work with the game's audio buses and effects — all without touching Godot's native audio system.

## Quick Start

```csharp
using BaseLib.Utils;

// Play an existing game sound
FmodAudio.PlayEvent("event:/sfx/heal");

// Play your own WAV/OGG/MP3 file through FMOD
FmodAudio.PlayFile("C:/path/to/my_sound.wav");

// Replace a game sound with your own file (no Harmony patch needed)
FmodAudio.RegisterFileReplacement("event:/sfx/ui/clicks/ui_click", "C:/path/to/my_click.wav");

// Load a custom FMOD Studio bank and play events from it
FmodAudio.LoadBank("C:/path/to/MyMod.strings.bank");
FmodAudio.LoadBank("C:/path/to/MyMod.bank");
FmodAudio.PlayEvent("event:/mods/mymod/explosion");
```

That's it for the basics. `FmodAudio` handles the FmodServer singleton lookup, file loading, caching, and error logging automatically.

## Playing Existing Game Sounds

The game has 563 FMOD events across its banks. You can trigger any of them:

```csharp
// Simple fire-and-forget
FmodAudio.PlayEvent("event:/sfx/block_gain");
FmodAudio.PlayEvent("event:/sfx/heal");
FmodAudio.PlayEvent("event:/sfx/buff");
FmodAudio.PlayEvent("event:/sfx/debuff");
FmodAudio.PlayEvent("event:/sfx/ui/clicks/ui_click");
FmodAudio.PlayEvent("event:/sfx/npcs/merchant/merchant_welcome");

// With parameters — some events change behavior based on params
FmodAudio.PlayEvent("event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_base", new()
{
    { "EnemyImpact_Intensity", 2f }  // 0=Light, 1=Medium, 2=Heavy
});

// With cooldown — prevents stacking when triggered rapidly
FmodAudio.PlayEvent("event:/sfx/buff", cooldownMs: 100);
```

### Common Event Paths

| Category | Path Pattern | Example |
|----------|-------------|---------|
| Combat | `event:/sfx/block_gain`, `block_break`, `block_hit`, `buff`, `debuff`, `heal` | `FmodAudio.PlayEvent("event:/sfx/buff")` |
| UI | `event:/sfx/ui/clicks/ui_click`, `ui_hover`, `ui_back` | |
| Gold | `event:/sfx/ui/gold/gold_1` (small), `gold_2` (medium), `gold_3` (large) | |
| Map | `event:/sfx/ui/map/map_open`, `map_close`, `map_select` | |
| Merchant | `event:/sfx/npcs/merchant/merchant_welcome`, `merchant_passive`, `merchant_thank_yous` | |
| Characters | `event:/sfx/characters/{id}/{id}_attack`, `_cast`, `_die`, `_select` | `event:/sfx/characters/regent/regent_forge` |
| Monsters | `event:/sfx/enemy/enemy_attacks/{id}/{id}_attack`, `_die`, `_cast` | |
| Damage types | `event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_{type}` | armor, fur, insect, magic, plant, slime, stone |
| Ambience | `event:/sfx/ambience/act1_ambience`, `act2_ambience`, `act3_ambience` | |
| Music | `event:/music/act1_a1_v1`, `act2_a1_v2`, etc. | |
| Cards | `event:/sfx/ui/cards/card_transform` | |
| Relics | `event:/sfx/ui/relic_activate_general`, `relic_activate_draw` | |

### Checking If an Event Exists

```csharp
if (FmodAudio.EventExists("event:/sfx/characters/mychar/mychar_attack"))
{
    FmodAudio.PlayEvent("event:/sfx/characters/mychar/mychar_attack");
}
else
{
    FmodAudio.PlayEvent("event:/sfx/characters/attack_fire");
}
```

## Playing Custom Audio Files

You can load WAV, OGG, MP3, and other FMOD-supported formats directly from disk. The file gets loaded into FMOD's memory and plays through the same audio engine as the game's sounds.

```csharp
var modDir = Path.GetDirectoryName(typeof(MyMod).Assembly.Location);

// Play a custom sound — loads on first call, cached after that
FmodAudio.PlayFile(Path.Combine(modDir, "sounds", "custom_attack.wav"));

// With cooldown to prevent rapid stacking
FmodAudio.PlayFile(Path.Combine(modDir, "sounds", "trigger.wav"), cooldownMs: 150);
```

### Controlling Playback

`PlayFile` returns the FMOD sound handle, which you can use to adjust volume, pitch, or stop it:

```csharp
var sound = FmodAudio.PlayFile(Path.Combine(modDir, "sounds", "ambient_loop.ogg"));
if (sound != null)
{
    sound.Call("set_volume", 0.5f);    // 0.0 to 1.0
    sound.Call("set_pitch", 1.2f);     // 1.0 = normal, 2.0 = octave up, 0.5 = octave down

    // Later...
    if ((bool)sound.Call("is_playing"))
        sound.Call("stop");

    sound.Call("release");             // Free FMOD resources when done
}
```

You can also set volume and pitch directly in the PlayFile call:

```csharp
FmodAudio.PlayFile(myPath, volume: 0.7f, pitch: 0.9f);
```

### Streaming for Long Tracks

`PlayFile` loads the entire file into memory — fine for short sound effects, wasteful for multi-minute music. Use `PlayMusic` instead, which streams from disk:

```csharp
// Short SFX: load into memory (fast playback, uses RAM)
FmodAudio.PlayFile("sounds/hit.wav");

// Long music/ambience: stream from disk (low memory, slight disk I/O)
var music = FmodAudio.PlayMusic("sounds/boss_theme.ogg");

// Preload during init to avoid first-play hitch
FmodAudio.PreloadFile("sounds/hit.wav");         // loads into memory
FmodAudio.PreloadMusic("sounds/boss_theme.ogg"); // prepares for streaming
```

### FmodSound Handle Methods

The object returned by `PlayFile`, `PlayMusic`, and `CreateSoundInstance` supports:

| Method | Args | Description |
|--------|------|-------------|
| `play` | — | Start playback |
| `stop` | — | Stop playback |
| `set_volume` | `float` | Volume (0.0 – 1.0) |
| `get_volume` | — | Returns current volume |
| `set_pitch` | `float` | Pitch multiplier (1.0 = normal) |
| `get_pitch` | — | Returns current pitch |
| `set_paused` | `bool` | Pause/unpause |
| `is_playing` | — | Returns bool |
| `is_valid` | — | Check if handle is still valid |
| `release` | — | Free FMOD resources |

## Sound Replacement Registry

The most common modding use case: replacing a game sound with your own. Instead of writing Harmony patches yourself, register replacements and BaseLib handles the hooking:

```csharp
var modDir = Path.GetDirectoryName(typeof(MyMod).Assembly.Location)!;

// Replace the UI click with a custom file
FmodAudio.RegisterFileReplacement(
    "event:/sfx/ui/clicks/ui_click",
    Path.Combine(modDir, "sounds", "my_click.wav")
);

// Replace one game event with another
FmodAudio.RegisterEventReplacement(
    "event:/sfx/heal",
    "event:/sfx/buff"  // healing now sounds like a buff
);

// Custom logic — return true to suppress the original, false to let it through
FmodAudio.RegisterReplacement("event:/sfx/block_gain", (eventPath, volume) =>
{
    if (volume > 0.5f)
    {
        // Big block: play a custom beefy sound
        FmodAudio.PlayFile(Path.Combine(modDir, "sounds", "big_block.wav"), volume);
        return true;
    }
    return false; // small block: let the original play
});

// Remove when done
FmodAudio.RemoveReplacement("event:/sfx/ui/clicks/ui_click");
```

Multiple mods can register replacements without conflicting Harmony patches. Last registration wins for a given event path.

## Sound Pools (Random Selection)

Play a random sound from a set, with automatic no-repeat logic so the same sound doesn't play twice in a row:

```csharp
var modDir = Path.GetDirectoryName(typeof(MyMod).Assembly.Location)!;

// Create a pool of attack grunts
FmodAudio.CreatePool("my_attack_sounds",
    Path.Combine(modDir, "sounds", "attack_1.wav"),
    Path.Combine(modDir, "sounds", "attack_2.wav"),
    Path.Combine(modDir, "sounds", "attack_3.wav"),
    Path.Combine(modDir, "sounds", "attack_4.wav")
);

// Play a random one each time
FmodAudio.PlayPool("my_attack_sounds");

// With volume/pitch
FmodAudio.PlayPool("my_attack_sounds", volume: 0.8f, pitch: 1.1f);

// With cooldown (e.g. on-hit sound that shouldn't stack)
FmodAudio.PlayPool("my_attack_sounds", cooldownMs: 80);

// Mix FMOD events and custom files in the same pool
FmodAudio.CreatePool("death_sounds",
    "event:/sfx/enemy/enemy_attacks/axebot/axebot_die",  // existing game event
    Path.Combine(modDir, "sounds", "custom_death.wav")    // custom file
);

// Add more sounds to an existing pool later
FmodAudio.AddToPool("my_attack_sounds",
    Path.Combine(modDir, "sounds", "attack_5.wav")
);
```

You can also combine pools with the replacement registry:

```csharp
FmodAudio.CreatePool("heal_variants", "sounds/heal_1.wav", "sounds/heal_2.wav", "sounds/heal_3.wav");
FmodAudio.RegisterReplacement("event:/sfx/heal", (_, volume) =>
{
    FmodAudio.PlayPool("heal_variants", volume: volume);
    return true;
});
```

## Snapshots (Mixer Effects)

FMOD snapshots apply global mixer effects — ducking, low-pass filters, reverb swells, etc. The game uses `snapshot:/pause` to muffle audio when paused. You can trigger snapshots for custom gameplay moments:

```csharp
// Start a snapshot (e.g. during a boss phase transition)
var snapshot = FmodAudio.StartSnapshot("snapshot:/pause");

// ... do your dramatic thing ...

// Stop it, letting FMOD's release envelope handle the transition
FmodAudio.StopSnapshot(snapshot, allowFadeout: true);

// Or cut immediately
FmodAudio.StopSnapshot(snapshot, allowFadeout: false);
```

If you build custom FMOD banks, you can define your own snapshots with whatever effects you want (heavy reverb for a cave encounter, low-pass for underwater, etc.) and trigger them the same way.

## Cooldowns / Throttling

When a game effect triggers audio rapidly (a power that fires on every card played, a relic that procs on every hit), you get ugly sound stacking. Cooldowns silently skip plays that are too close together:

```csharp
// These all support cooldownMs:
FmodAudio.PlayEvent("event:/sfx/buff", cooldownMs: 100);
FmodAudio.PlayFile("sounds/proc.wav", cooldownMs: 150);
FmodAudio.PlayPool("hit_sounds", cooldownMs: 80);
```

The cooldown is per-path — `PlayEvent("event:/sfx/buff", 100)` and `PlayEvent("event:/sfx/debuff", 100)` track independently.

## Bus Control

STS2's FMOD mixer has these buses:

| Bus | Path | Purpose |
|-----|------|---------|
| Master | `bus:/master` | Top-level mix |
| SFX | `bus:/master/sfx` | All sound effects |
| Music | `bus:/master/music` | Background music |
| Ambience | `bus:/master/ambience` | Environmental sounds |
| Reverb | `bus:/master/sfx/Reverb` | SFX routed through reverb |
| Chorus | `bus:/master/sfx/chorus` | SFX with chorus effect |
| SFX Ducking (Bass) | `bus:/master/sfx_ducking_bass` | Ducks under music bass |
| SFX Ducking (Big) | `bus:/master/sfx_ducking_big` | Ducks during big hits |

### Adjusting Bus Volume

```csharp
// Set SFX volume to 50%
FmodAudio.SetBusVolume("bus:/master/sfx", 0.5f);

// Read current music volume
float musicVol = FmodAudio.GetBusVolume("bus:/master/music");

// Mute ambience
FmodAudio.SetBusMute("bus:/master/ambience", true);

// Pause all SFX (useful during cutscenes)
FmodAudio.SetBusPaused("bus:/master/sfx", true);
```

### Direct Bus Access

For more control, get the bus object directly:

```csharp
var reverbBus = FmodAudio.GetBus("bus:/master/sfx/Reverb");
if (reverbBus != null)
{
    reverbBus.Call("set_volume", 1.5f);  // boost reverb send
    reverbBus.Call("set_paused", false);
}
```

If you build FMOD Studio banks, route your custom events through these buses so they respect the player's volume settings. For instance, routing a custom SFX through `bus:/master/sfx/Reverb` gives it the game's reverb treatment automatically.

## Global Parameters

The game uses global FMOD parameters to drive adaptive audio. You can read and write them:

```csharp
// Music progress — controls which section of the act music plays
// 0=Init, 1=Enemy, 2=Merchant, 3=Rest, 4=Unknown, 5=Treasure,
// 6=Elite, 7=CombatEnd, 8=Elite2, 9=MerchantEnd
FmodAudio.SetGlobalParameter("Progress", 6f);  // switch to elite music

// Some parameters support named labels instead of raw numbers
FmodAudio.SetGlobalParameterByLabel("Progress", "Elite");

// SFX ducking (lower SFX volume when music is intense)
FmodAudio.SetGlobalParameter("sfx_duck", 1f);
FmodAudio.SetGlobalParameter("sfx_duck_big", 1f);

// Read current value
float progress = FmodAudio.GetGlobalParameter("Progress");
```

## Mute / Pause All

```csharp
FmodAudio.MuteAll();     // Mute every FMOD event
FmodAudio.UnmuteAll();

FmodAudio.PauseAll();    // Freeze all audio (good for a custom pause screen)
FmodAudio.UnpauseAll();
```

## Event Instances (Looping & Controllable Sounds)

For sounds you need ongoing control over (loops, music, ambience), create an event instance instead of using fire-and-forget:

```csharp
var loop = FmodAudio.CreateEventInstance("event:/sfx/ambience/act1_ambience");
if (loop != null)
{
    loop.Call("set_volume", 0.6f);
    loop.Call("start");

    // Change parameters while it's playing
    loop.Call("set_parameter_by_name", "some_param", 0.5f);

    // Stop with fadeout (FMOD handles the fade based on the event's AHDSR)
    loop.Call("stop", 0);  // 0 = FMOD_STUDIO_STOP_ALLOWFADEOUT
    // Or stop immediately
    loop.Call("stop", 1);  // 1 = FMOD_STUDIO_STOP_IMMEDIATE

    loop.Call("release");
}
```

### FmodEvent Instance Methods

| Method | Args | Description |
|--------|------|-------------|
| `start` | — | Start the event |
| `stop` | `int mode` | 0 = allow fadeout, 1 = immediate |
| `set_volume` | `float` | Volume override |
| `set_pitch` | `float` | Pitch multiplier |
| `set_paused` | `bool` | Pause/resume |
| `is_paused` | — | Returns bool |
| `get_playback_state` | — | Returns playback state int |
| `set_parameter_by_name` | `string, float` | Set a local parameter |
| `get_parameter_by_name` | `string` | Get a local parameter value |
| `release` | — | Release when done |
| `is_valid` | — | Check validity |

## Loading Custom FMOD Banks

If you build your own FMOD Studio project (requires FMOD Studio 2.03.x to match the game), you can load your banks at runtime and play events from them just like game sounds.

```csharp
var modDir = Path.GetDirectoryName(typeof(MyMod).Assembly.Location)!;
var banksDir = Path.Combine(modDir, "banks");

// Load order matters: strings bank first, then content banks
FmodAudio.LoadBank(Path.Combine(banksDir, "MyMod.strings.bank"));
FmodAudio.LoadBank(Path.Combine(banksDir, "MyMod.bank"));

// Now your custom events are available
FmodAudio.PlayEvent("event:/mods/mymod/custom_attack");
FmodAudio.PlayEvent("event:/mods/mymod/boss_music");

// Unload when done
FmodAudio.UnloadBank(Path.Combine(banksDir, "MyMod.bank"));
FmodAudio.UnloadBank(Path.Combine(banksDir, "MyMod.strings.bank"));
```

### Why Use Banks Over Raw Files?

Raw audio files via `PlayFile` give you basic play/stop/volume/pitch. FMOD Studio banks give you everything the game's sounds have:

- **Randomization**: Multiple sound variants that FMOD picks between automatically
- **Layering**: Stack multiple sounds into a single event
- **Effects**: Reverb, chorus, EQ, compression, distortion, all baked into the event
- **Adaptive music**: Transitions, stems, stingers driven by parameters
- **Bus routing**: Send sounds through the game's SFX/Music/Ambience/Reverb buses
- **Snapshots**: Custom mixer effects you can trigger at runtime

### FMOD Studio Setup

1. Download FMOD Studio **2.03.x** (must match the game's runtime — other versions will crash)
2. Create a new project
3. Namespace your events: `event:/mods/yourmod/...`
4. Build banks for the Desktop platform
5. Ship the `.bank` and `.strings.bank` files with your mod

## DSP Buffer Tuning

If players on lower-end hardware report audio crackling or popping, you can adjust the DSP buffer size. Larger buffers smooth out audio at the cost of slightly higher latency:

```csharp
// Check current settings
var (length, count) = FmodAudio.GetDspBufferSettings();
MainFile.Logger.Info($"DSP buffer: {length} samples x {count} buffers");

// Increase buffer for stability (default is usually 1024 x 4)
FmodAudio.SetDspBufferSize(2048, 4);
```

This could be tied into a mod config slider via BaseLib's config system.

## Performance Monitoring

For debugging audio-heavy mods, you can pull raw performance data from FMOD:

```csharp
var perf = FmodAudio.GetPerformanceData();
MainFile.Logger.Info($"FMOD performance: {perf}");
```

This reports CPU usage, memory allocation, active channel count, etc. Useful for catching audio leaks where sounds are created but never released.

## Practical Examples

### Custom Character with Sound Pool

```csharp
[ModInitializer(nameof(Init))]
public static class MyCharMod
{
    private static string _modDir = null!;

    public static void Init()
    {
        _modDir = Path.GetDirectoryName(typeof(MyCharMod).Assembly.Location)!;

        // Preload all character sounds
        foreach (var file in Directory.GetFiles(Path.Combine(_modDir, "sounds"), "*.wav"))
            FmodAudio.PreloadFile(file);

        // Create sound pools for variety
        FmodAudio.CreatePool("mychar_attacks",
            Sound("attack_1.wav"), Sound("attack_2.wav"),
            Sound("attack_3.wav"), Sound("attack_4.wav")
        );

        FmodAudio.CreatePool("mychar_hits",
            Sound("hit_light.wav"), Sound("hit_medium.wav"), Sound("hit_heavy.wav")
        );

        // Replace the character's auto-generated sound paths
        FmodAudio.RegisterReplacement("event:/sfx/characters/mychar/mychar_attack", (_, vol) =>
        {
            FmodAudio.PlayPool("mychar_attacks", volume: vol);
            return true;
        });
    }

    private static string Sound(string name) => Path.Combine(_modDir, "sounds", name);
}
```

### Boss Fight with Custom Music and Snapshots

```csharp
private GodotObject? _bossMusic;
private GodotObject? _bossSnapshot;

void OnBossFightStart()
{
    var modDir = Path.GetDirectoryName(typeof(MyMod).Assembly.Location)!;

    // Load our music bank
    FmodAudio.LoadBank(Path.Combine(modDir, "banks", "boss.bank"));

    // Start dramatic snapshot (defined in our bank — reverb + low-pass)
    _bossSnapshot = FmodAudio.StartSnapshot("snapshot:/mods/mymod/boss_intro");

    // Fade out the current act music, then start ours
    FmodAudio.SetBusVolume("bus:/master/music", 0f);
    _bossMusic = FmodAudio.PlayMusic(Path.Combine(modDir, "music", "boss_theme.ogg"));

    // After the intro, transition the snapshot
    // (in practice you'd do this on a timer or game event)
}

void OnBossPhaseTwo()
{
    // Kill the intro snapshot, start the intense one
    FmodAudio.StopSnapshot(_bossSnapshot, allowFadeout: true);
    _bossSnapshot = FmodAudio.StartSnapshot("snapshot:/mods/mymod/boss_phase2");

    // Pitch up the music slightly for intensity
    _bossMusic?.Call("set_pitch", 1.05f);
}

void OnBossFightEnd()
{
    FmodAudio.StopSnapshot(_bossSnapshot);

    _bossMusic?.Call("stop");
    _bossMusic?.Call("release");
    _bossMusic = null;

    // Restore game music volume
    FmodAudio.SetBusVolume("bus:/master/music", 0.25f);
}
```

### Replacing All of a Monster's Sounds

```csharp
public static void Init()
{
    var modDir = Path.GetDirectoryName(typeof(MyMod).Assembly.Location)!;

    // Replace every sound for a custom monster
    var monsterId = "my_monster";
    var basePath = $"event:/sfx/enemy/enemy_attacks/{monsterId}/{monsterId}";

    FmodAudio.RegisterFileReplacement($"{basePath}_attack", Sound("monster_attack.ogg"));
    FmodAudio.RegisterFileReplacement($"{basePath}_die", Sound("monster_death.ogg"));
    FmodAudio.RegisterFileReplacement($"{basePath}_cast", Sound("monster_cast.ogg"));

    // Create a pool for the damage sound with per-material variants
    FmodAudio.CreatePool("my_monster_hit",
        Sound("hit_stone_1.wav"), Sound("hit_stone_2.wav"), Sound("hit_stone_3.wav")
    );
    FmodAudio.RegisterReplacement(
        "event:/sfx/enemy/enemy_impact_enemy_size/enemy_impact_stone",
        (_, vol) => { FmodAudio.PlayPool("my_monster_hit", volume: vol); return true; }
    );
}
```

### Mod Volume Config

```csharp
// In your mod config (using BaseLib's config system)
[ConfigSlider("Mod SFX Volume", 0f, 1f)]
public float ModVolume { get; set; } = 1.0f;

// Play sounds through your own volume multiplier
FmodAudio.PlayFile(soundPath, volume: MyConfig.ModVolume);

// Or for bank events, use an event instance with volume control
var evt = FmodAudio.CreateEventInstance("event:/mods/mymod/my_sound");
evt?.Call("set_volume", MyConfig.ModVolume);
evt?.Call("start");
evt?.Call("release");
```

## Architecture Reference

### How STS2's Audio Pipeline Works

```
C# Game Code (SfxCmd, NAudioManager, NRunMusicController)
    ↓ calls Proxy node methods
GDScript Proxy (audio_manager_proxy.gd)
    ↓ calls singleton
FmodServer (GDExtension: libGodotFmod)
    ↓ wraps
FMOD Studio Runtime (fmod.dll + fmodstudio.dll)
    ↓ reads from
.bank files (res://banks/desktop/*)
```

`FmodAudio` talks to `FmodServer` directly, skipping the GDScript proxy layer. The replacement registry patches `NAudioManager.PlayOneShot` so it intercepts game sounds before they reach the proxy.

### Bank Layout

| Bank | Events | Contents |
|------|--------|----------|
| Master.bank | 3 | Bus definitions, snapshots, routing |
| Master.strings.bank | — | Event path lookup (612 entries) |
| sfx.bank | 538 | All sound effects |
| temp_sfx.bank | 2 | Debug/placeholder sounds |
| ambience.bank | 6 | Environmental loops |
| act1_a1–act3_a2 | 2–4 each | Per-act music variants |

## Full API Reference

### Core Playback
- `PlayEvent(string eventPath) → bool`
- `PlayEvent(string eventPath, Dictionary<string, float> parameters) → bool`
- `PlayEvent(string eventPath, int cooldownMs) → bool`
- `PlayEventByGuid(string guid) → bool`
- `PlayFile(string absolutePath, float volume = 1.0f, float pitch = 1.0f) → GodotObject?`
- `PlayFile(string absolutePath, int cooldownMs, float volume = 1.0f, float pitch = 1.0f) → GodotObject?`
- `PlayMusic(string absolutePath, float volume = 1.0f, float pitch = 1.0f) → GodotObject?`

### Loading & Caching
- `PreloadFile(string absolutePath) → bool` — load into memory
- `PreloadMusic(string absolutePath) → bool` — prepare for streaming
- `CreateSoundInstance(string absolutePath) → GodotObject?`
- `CreateEventInstance(string eventPath) → GodotObject?`
- `UnloadFile(string absolutePath)`

### Banks
- `LoadBank(string bankPath) → bool`
- `UnloadBank(string bankPath)`

### Sound Replacement
- `RegisterReplacement(string originalEvent, Func<string, float, bool> handler)`
- `RegisterFileReplacement(string originalEvent, string replacementFilePath)`
- `RegisterEventReplacement(string originalEvent, string replacementEvent)`
- `RemoveReplacement(string originalEvent)`
- `ClearReplacements()`

### Sound Pools
- `CreatePool(string poolName, params string[] soundPaths)`
- `AddToPool(string poolName, params string[] soundPaths)`
- `PlayPool(string poolName, float volume = 1.0f, float pitch = 1.0f) → GodotObject?`
- `PlayPool(string poolName, int cooldownMs, float volume = 1.0f, float pitch = 1.0f) → GodotObject?`
- `RemovePool(string poolName)`

### Snapshots
- `StartSnapshot(string snapshotPath) → GodotObject?`
- `StopSnapshot(GodotObject? snapshot, bool allowFadeout = true)`

### Bus Control
- `GetBus(string busPath) → GodotObject?`
- `SetBusVolume(string busPath, float volume)`
- `GetBusVolume(string busPath) → float`
- `SetBusMute(string busPath, bool muted)`
- `SetBusPaused(string busPath, bool paused)`
- `BusExists(string busPath) → bool`

### Global Parameters
- `SetGlobalParameter(string name, float value)`
- `SetGlobalParameterByLabel(string name, string label)`
- `GetGlobalParameter(string name) → float`

### Global Mute/Pause
- `MuteAll()` / `UnmuteAll()`
- `PauseAll()` / `UnpauseAll()`

### DSP & Performance
- `SetDspBufferSize(int bufferLength, int numBuffers)`
- `GetDspBufferSettings() → (int bufferLength, int numBuffers)`
- `GetPerformanceData() → Variant`

### Utilities
- `EventExists(string eventPath) → bool`
- `IsAvailable → bool`
- `UnloadAll()`
