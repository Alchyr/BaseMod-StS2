# Scene Auto-Conversion

Automatically converts Godot scenes with standard root node types (Node2D, Control) into game-specific types (NCreatureVisuals, NEnergyCounter) at instantiation time. Modders can build scenes in the Godot editor using standard nodes and have them work seamlessly when the game calls `Instantiate<T>()`.

## The Problem

When the game calls `PackedScene.Instantiate<NCreatureVisuals>()`, it expects the scene's root node to be an `NCreatureVisuals`. But modders building scenes in the Godot editor can't use game-specific script types as root nodes — those scripts aren't available in the editor. So they use `Node2D` or `Control` as the root, and the cast fails with an `InvalidCastException`.

Harmony can't patch `Instantiate<T>()` directly because it's a generic method and .NET shares native code across reference-type instantiations. Previous attempts to return wrapper classes with explicit cast operators also failed because `Instantiate<T>()` uses an IL `castclass` instruction, which ignores user-defined operators.

## How It Works

The solution patches the **non-generic** `PackedScene.Instantiate(GenEditState)` with a Harmony postfix. Since Godot's generic `Instantiate<T>()` calls the non-generic version internally:

```csharp
// Godot's actual implementation (from GodotSharp.dll):
public T Instantiate<T>(GenEditState editState = ...) where T : class
{
    return (T)(object)Instantiate(editState);  // cast happens AFTER our postfix
}
```

The postfix runs after `Instantiate()` returns but before `Instantiate<T>()` casts the result. If the scene path is registered for auto-conversion, the postfix replaces the Node2D with a proper NCreatureVisuals (or whatever type is registered). The cast then succeeds naturally.

### Step by step

1. Game code calls `scene.Instantiate<NCreatureVisuals>()`
2. Internally, `Instantiate<T>()` calls `Instantiate()` (non-generic)
3. Non-generic `Instantiate()` returns a `Node2D` (the scene's actual root type)
4. **Our Harmony postfix fires** — checks the scene path against the registry
5. Finds a match, calls `NCreatureVisualsFactory.CreateAndConvert(node2d)`
6. Factory creates a new `NCreatureVisuals`, transfers children, generates missing marker nodes
7. Postfix replaces `__result` with the converted `NCreatureVisuals`
8. Back in `Instantiate<T>()`, the `(T)(object)result` cast succeeds because result IS an NCreatureVisuals

## Usage

### For Custom Characters (automatic)

If you extend `CustomCharacterModel` and set `CustomVisualPath`, the path is auto-registered when your character is created. No extra code needed:

```csharp
public class MyCharacter : CustomCharacterModel
{
    // BaseLib auto-registers this path for NCreatureVisuals conversion
    public override string? CustomVisualPath => "res://MyMod/scenes/my_character.tscn";

    // Return null so the game's default CreateVisuals() path runs,
    // which calls Instantiate<NCreatureVisuals>() and triggers auto-conversion.
    public override NCreatureVisuals? CreateCustomVisuals() => null;
}
```

`CustomEnergyCounterPath` is also auto-registered for `NEnergyCounter` conversion.

### For Other Scenes (manual registration)

Register scene paths during your mod's initialization:

```csharp
using BaseLib.Utils.NodeFactories;

// In your mod's Initialize():
NodeFactory.RegisterSceneType<NCreatureVisuals>("res://MyMod/scenes/my_monster.tscn");
NodeFactory.RegisterSceneType<NEnergyCounter>("res://MyMod/scenes/my_energy.tscn");
NodeFactory.RegisterSceneType<Control>("res://MyMod/scenes/my_ui.tscn");
```

After registration, any call to `Instantiate<NCreatureVisuals>()` on that scene path will auto-convert.

### Using CreateFromScene Directly

You can also bypass auto-conversion and call the factory explicitly. This is what `CreateCustomVisuals()` does internally when you don't return null:

```csharp
var visuals = NodeFactory<NCreatureVisuals>.CreateFromScene("res://MyMod/scenes/my_creature.tscn");
```

Both approaches use the same factory logic. Auto-conversion just makes it transparent for code paths you don't control (Bestiary, GameOverScreen, etc).

## Scene Requirements

Your `.tscn` scene should include the child nodes that the target type expects. For `NCreatureVisuals`:

- **Visuals** (Node2D or Sprite2D) — the visual representation, with `unique_name_in_owner = true`
- **Bounds** (Control) — clickable area for targeting

The factory will auto-generate missing marker nodes (`CenterPos`, `IntentPos`, `OrbPos`, `TalkPos`) with reasonable defaults based on the Bounds size. But `Visuals` must be provided.

For `NEnergyCounter`, see the existing `NEnergyCounterFactory` for required nodes.

## Public API

```csharp
// Registration
NodeFactory.RegisterSceneType<NCreatureVisuals>(scenePath);
NodeFactory.RegisterSceneType(scenePath, typeof(NCreatureVisuals));
NodeFactory.UnregisterSceneType(scenePath);

// Queries
NodeFactory.HasFactory<NCreatureVisuals>();  // true if a factory exists for this type
NodeFactory.IsRegistered(scenePath);         // true if this path is registered

// Direct factory use (no auto-conversion needed)
var node = NodeFactory<NCreatureVisuals>.CreateFromScene(scenePath);
var node = NodeFactory<NCreatureVisuals>.CreateFromScene(packedScene);
var node = NodeFactory<NCreatureVisuals>.CreateFromResource(texture2d);
```

## Supported Types

Auto-conversion is available for any type that has a registered `NodeFactory<T>`:

| Type | Factory | Description |
|------|---------|-------------|
| `NCreatureVisuals` | `NCreatureVisualsFactory` | Monster and character combat visuals |
| `NEnergyCounter` | `NEnergyCounterFactory` | Energy counter UI |
| `Control` | `ControlFactory` | Generic UI elements |

Custom factories can be added by extending `NodeFactory<T>`.

## How It Interacts With Existing Code

### With `CreateFromScene()`

If a scene is both registered for auto-conversion AND used via `CreateFromScene()`, the postfix fires on the internal `Instantiate()` call and converts the node. `CreateFromScene()` then sees `if (n is T t) return t;` is true and returns immediately. Both paths produce the same result.

### With `[GlobalClass]` Scenes

If your scene root already uses a `[GlobalClass]` C# script that extends `NCreatureVisuals`, `Instantiate()` returns the correct type directly. The postfix's `IsAssignableFrom` check passes and it returns without converting. No overhead beyond a dictionary lookup.

### With Vanilla Scenes

Vanilla scenes aren't registered in the scene type dictionary, so the postfix returns immediately after the `TryGetValue` miss. The overhead is one `ConcurrentDictionary.TryGetValue()` call per `Instantiate()` — negligible.

## Thread Safety

- Both the factory registry and scene type registry use `ConcurrentDictionary`
- The `_isConverting` recursion guard is `[ThreadStatic]` — each thread has its own flag
- Godot node creation is main-thread in practice, but the system is safe if background asset loading calls `Instantiate`

## Self-Tests

17 self-tests run automatically during `NodeFactory.Init()`:

1. Node2D to NCreatureVisuals conversion (non-generic Instantiate)
2. **Generic `Instantiate<NCreatureVisuals>()` chain** — the critical test proving the IL castclass works
3. Node2D to Control conversion
4. Already-correct-type passthrough (no unnecessary conversion)
5. Unregistered scene passthrough (no interference)
6. Exact type verification
7. Null/empty/whitespace path rejection
8. Registration overwrite
9. Unregister + double-unregister safety
10. HasFactory for registered and unregistered types
11. IsRegistered for various path states

Results are logged at startup: `[BaseLib] All 17 auto-conversion self-tests passed`

## Known Limitations

- **Scene must be loadable**: The scene path must exist in a loaded PCK or the game's resource filesystem. If `GetScene()` fails, auto-conversion never fires.
- **No pattern-based registration**: Paths must be registered exactly. There's no wildcard/glob support (e.g., `res://MyMod/creatures/*`).
- **Factory must exist for target type**: If you register a scene for a type with no factory, a warning is logged and the scene is returned unconverted.
- **One type per path**: Each scene path maps to exactly one target type. Registering the same path again overwrites the previous type.
- **Properties not fully copied**: The factory copies common properties (Name, position, scale, modulate, etc.) from the source root to the converted target, but game-type-specific properties set on the source root are lost. Put important configuration on child nodes instead.
- **No animation auto-setup**: The factory converts the node structure but doesn't create AnimationPlayers or set up animation states. If your creature needs animations, either add an AnimationPlayer to the scene or set it up in code after getting the NCreatureVisuals back.
- **First-conversion-only logging**: To avoid log spam, only the first conversion per scene path is logged at Info level. Subsequent conversions for the same path are silent.

## Potential Future Improvements

- **Pattern-based registration**: `RegisterScenePattern("res://MyMod/creatures/*", typeof(NCreatureVisuals))` to auto-register all scenes under a directory
- **CustomMonsterModel abstract class**: Similar to `CustomCharacterModel` but for monsters, with auto-registration of VisualsPath
- **Auto-detection from scene content**: Detect NCreatureVisuals-compatible scenes by checking for `%Visuals` and `%Bounds` children, without requiring explicit registration
- **NEnergyCounter generic chain test**: Currently only NCreatureVisuals and Control are tested via the generic `Instantiate<T>()` path
- **Conversion event hooks**: `NodeFactory.OnConverted += (path, from, to) => ...` for mods that want to post-process converted nodes
