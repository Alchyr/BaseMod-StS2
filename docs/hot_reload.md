# Hot Reload

Change your mod's code while the game is running and see the results instantly — no
restart needed. Edit a card's damage, tweak a relic's effect, fix a bug in a power,
and it takes effect in seconds.

## Do I Need to Do Anything Special?

**If your mod already uses the BaseLib NuGet package: almost nothing.**

BaseLib automatically handles assembly name stamping and deployment. The only thing
you need is `Sts2Path` set in your `.csproj` so the build knows where the game is
installed. Most mod templates already have this.

If you're starting from the [Mod Template](https://github.com/Alchyr/ModTemplate-StS2),
everything is pre-configured and hot reload works out of the box.

## Quick Start

### 1. Make sure your csproj has the game path

Your `.csproj` should have something like this (adjust the path for your system):

```xml
<PropertyGroup>
    <Sts2Path>E:\SteamLibrary\steamapps\common\Slay the Spire 2</Sts2Path>
</PropertyGroup>
```

Most templates detect the Steam path automatically. If yours doesn't, add it manually.

### 2. Build your mod

Build in Debug mode like normal:

```
dotnet build -c Debug
```

You'll see output like:

```
MyMod -> bin\Debug\net9.0\MyMod_hr143052789.dll
[BaseLib] Deployed to .../mods/MyMod/
```

The `_hr143052789` suffix is the hot reload stamp — a unique name per build so the
game can load it alongside the previous version. This happens automatically; you
don't need to do anything.

### 3. Reload in-game

Open the dev console (`~` key) and type:

```
hotreload MyMod
```

That's it. Your changes are now live in the game.

## What Happens During a Hot Reload

When you run `hotreload MyMod`, BaseLib:

1. Finds the newest DLL in `mods/MyMod/`
2. Loads it into the game's runtime
3. Detects which entities changed (by comparing code signatures)
4. Removes old versions of changed entities from the game database
5. Creates fresh instances of changed entities (unchanged ones are skipped)
6. Re-registers them in card/relic/potion pools
7. Reloads localization text
8. Updates any cards, relics, or powers visible in the current combat

All of this typically takes under 100ms.

## Console Commands

| Command | Description |
|---------|-------------|
| `hotreload MyMod` | Reload a mod by its folder name in `mods/` |
| `hotreload path/to/MyMod.dll` | Reload a specific DLL by path |
| `hotreload MyMod 3` | Reload with tier 3 (includes PCK resource remount) |
| `hotreload_list` | Show all mods available for reload with timestamps |
| `hotreload_status` | Show the last reload result and file watcher state |
| `hotreload_test` | Run integration tests against the live game state |

### Tier Auto-Detection

If you don't specify a tier, BaseLib picks one automatically:

- **Tier 2** (default) — Reloads entities + Harmony patches + localization
- **Tier 3** (auto if `.pck` file exists) — Everything in tier 2 + Godot resources (scenes, textures, audio)

You can force a specific tier by passing it as the second argument:
`hotreload MyMod 1` for patch-only reload (fastest, no entity refresh).

## What Can Be Hot-Reloaded

| Change | Supported | Notes |
|--------|-----------|-------|
| Card stats (damage, cost, block) | Yes | Change the value, rebuild, reload |
| Card effects (OnPlay logic) | Yes | New method body takes effect |
| Relic effects | Yes | Same as cards |
| Power effects | Yes | Same as cards |
| Potion effects | Yes | Same as cards |
| Localization text | Yes | Updated via `ILocalizationProvider` |
| Harmony patches | Yes | Old patches removed, new ones applied |
| New entity types | Yes | Added to game database and pools |
| Removed entity types | Yes | Removed from database (orphaned instances warned) |
| Custom enums / keywords | Yes | Re-generated on reload |
| SavedSpireField registrations | Yes | Re-processed on reload |
| Custom character visuals | Partial | Paths re-registered; character selection UI needs restart |
| Custom orb properties | Yes | Random pool cache rebuilt |
| Scene registrations (NodeFactory) | Yes | Old registrations cleaned up |
| Godot scenes/textures (PCK) | Yes | Requires tier 3 reload |
| BaseLib dependency version changes | No | Requires game restart |
| Adding new NuGet packages | No | Requires game restart |

## Incremental Reload

BaseLib compares the code signature of each entity type between the old and new
assembly. If a type hasn't changed (same methods, fields, properties, IL bytecode),
it's skipped entirely. This means:

- Rebuilding without changes → 0 entities injected (instant)
- Changing one card out of 50 → only that card is re-injected
- Changing a shared utility class → all entities that use it get new IL → re-injected

## File Watcher (Automatic Reload)

Instead of typing `hotreload` every time, you can enable the file watcher to
automatically reload when you build:

1. Open BaseLib's settings in the mod menu
2. Enable **"File Watcher"**
3. Every time you `dotnet build`, the new DLL is detected and reloaded automatically

The watcher:
- Monitors the `mods/` directory for new hot-reload-stamped DLLs
- Debounces 500ms after the last file write (MSBuild writes incrementally)
- Works on Windows natively; includes a polling fallback for Linux
- Only triggers for `_hr`-stamped DLLs (won't react to manifest copies etc.)

## Troubleshooting

### "Hot reload already in progress"

The previous reload hasn't finished yet. Wait a few seconds and try again.

### "dll_not_found"

The DLL path doesn't exist. Check:
- Did the build succeed? (`dotnet build` should show `Build succeeded`)
- Is `Sts2Path` correct in your `.csproj`?
- Run `hotreload_list` to see what's actually in the mods folder

### "Entity staging failed"

An entity couldn't be created from the new assembly. Common causes:
- **MissingMethodException** — Your mod references a method that doesn't exist in
  the loaded version of a dependency. Rebuild against the same version.
- **DuplicateModelException** — Shouldn't happen (BaseLib removes old entities first),
  but if it does, restart the game and try again.
- **TypeLoadException** — A base class changed incompatibly. Restart the game.

Check the game log for the specific error: `[BaseLib] [HotReload] Failed to stage ...`

### "assembly_load" error

The DLL couldn't be loaded. Common causes:
- The DLL is corrupted or truncated (build was interrupted)
- A dependency DLL is missing from the mod folder
- The assembly references a game version that doesn't match

### Build says "file is locked"

The game holds a lock on the original mod DLL. This is normal — the hot reload
stamping gives each build a unique filename specifically to avoid this. If the
**stamped** DLL is also locked, it means the game loaded it during a previous
hot reload. Just build again (the new timestamp produces a new filename).

### Changes don't seem to take effect

- Make sure you're building in **Debug** mode (Release builds aren't stamped)
- Check that `hotreload` output says the entities were "injected", not "unchanged"
- If entities show as "unchanged", your code changes might be in a file that
  doesn't affect entity type signatures (e.g., a helper method in a non-entity class).
  In that case, the changed code IS loaded — it just didn't trigger re-injection
  because the entity types themselves didn't change.

### Memory usage grows over time

Each hot reload loads a new assembly into the game's runtime. Old assemblies can't be
fully unloaded (a .NET limitation for non-collectible ALCs). Each reload adds ~0.5-2 MB.
Restart the game periodically during long development sessions.

## Opting Out

If hot reload stamping causes issues with your build pipeline, disable it:

```xml
<PropertyGroup>
    <BaseLibHotReload>false</BaseLibHotReload>
</PropertyGroup>
```

To disable the auto-copy to mods folder:

```xml
<PropertyGroup>
    <BaseLibSkipModsCopy>true</BaseLibSkipModsCopy>
</PropertyGroup>
```

## For Advanced Users

### Public API

You can trigger hot reload programmatically from your own code:

```csharp
using BaseLib.HotReload;

// Reload by mod folder name (auto-detects tier and latest DLL)
var result = HotReloadEngine.ReloadByModId("MyMod");
if (result.Success)
    Console.WriteLine($"Reloaded {result.EntitiesInjected} entities in {result.TotalMs}ms");
else
    Console.WriteLine($"Failed: {result.Errors[0]}");

// Reload a specific DLL with explicit tier
var result2 = HotReloadEngine.Reload("path/to/MyMod_hr143052789.dll", tier: 2);

// Subscribe to reload events
HotReloadEngine.OnReloadComplete += result =>
{
    // Called after every reload (success or failure)
    // Use this to refresh your mod's own caches
};

// Check reload history
foreach (var past in HotReloadEngine.ReloadHistory)
    Console.WriteLine(past.Summary);
```

### HotReloadResult Fields

| Field | Type | Description |
|-------|------|-------------|
| `Success` | bool | Whether the reload completed without errors |
| `Tier` | int | The reload tier used (1, 2, or 3) |
| `AssemblyName` | string | Full name of the loaded assembly |
| `EntitiesInjected` | int | How many entities were created fresh |
| `EntitiesSkipped` | int | How many entities were unchanged (same hash) |
| `EntitiesRemoved` | int | How many old entities were removed from ModelDb |
| `PatchCount` | int | How many Harmony patches were applied |
| `PoolsUnfrozen` | int | How many game pools were unfrozen for re-registration |
| `LocalizationReloaded` | bool | Whether localization tables were refreshed |
| `PckReloaded` | bool | Whether the PCK resource pack was remounted |
| `LiveInstancesRefreshed` | int | How many in-scene nodes were updated |
| `TotalMs` | long | Total time in milliseconds |
| `StepTimings` | dict | Time per pipeline step (for profiling) |
| `ChangedEntities` | list | Per-entity details (name, action, id) |
| `Actions` | list | What the pipeline did (for debugging) |
| `Errors` | list | What went wrong (empty on success) |
| `Warnings` | list | Non-fatal issues (e.g., memory accumulation) |

### How Assembly Stamping Works

When you build in Debug mode, BaseLib's NuGet package injects a PropertyGroup that
changes your assembly name to include a timestamp:

```
MyMod → MyMod_hr143052789
```

This produces a file like `MyMod_hr143052789.dll`. Each build gets a unique name,
which solves two problems:
1. The game's runtime rejects loading an assembly with a name it already has
2. The game holds a file lock on the previously loaded DLL

The timestamp format is `HHmmssfff` (hours, minutes, seconds, milliseconds in UTC).
Release builds are NOT stamped — only Debug.

### Pipeline Steps (Technical)

1. **Load assembly** into the game's ALC (Godot's `IsolatedComponentLoadContext`)
2. **Stage Harmony patches** with a unique instance per reload
3. **Update `Mod.assembly`** reference in the game's `ModManager`
4. **Invalidate `ReflectionHelper._modTypes`** cache
5. **Register entity IDs** in the network serialization cache
6. **Remove old entities** from `ModelDb._contentById` (prevents DuplicateModelException)
7. **Create new entities** via `Activator.CreateInstance()` (BaseLib constructors auto-register in pools)
8. **Clear ModelDb caches** (14 lazy-computed collection fields)
9. **Unfreeze pools** and clean old entries from `ModHelper._moddedContentForPools`
10. **Refresh BaseLib subsystems** (SavedSpireField, ModInterop, custom enums, NodeFactory, characters, orbs)
11. **Reload localization** (file-based + ILocalizationProvider re-injection)
12. **Remount PCK** (tier 3 only)
13. **Verify entities** in ModelDb + ToMutable() sanity check on cards
14. **Refresh live instances** (scene tree nodes + run state: deck, piles, relics, powers)
15. **Commit session** (swap Harmony instances, clean stale patches, unload old ALC)

Rollback: if entity creation fails, the pipeline restores ModelDb, Mod.assembly
references, serialization cache, and Harmony patches to their pre-reload state.
