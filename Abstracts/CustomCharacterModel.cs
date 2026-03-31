using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using System.Reflection;
using BaseLib.Utils.NodeFactories;
using MegaCrit.Sts2.Core.Helpers;

namespace BaseLib.Abstracts;

public abstract class CustomCharacterModel : CharacterModel, ICustomModel, ILocalizationProvider
{
    public CustomCharacterModel()
    {
        ModelDbCustomCharacters.Register(this);
    }
    
    /// <summary>
    /// Override this to define localization directly in your class.
    /// You are recommended to return a CharacterLoc<seealso cref="CharacterLoc"/>.
    /// </summary>
    public virtual List<(string, string)>? Localization => null;

    /// <summary>
    /// Override this or place your scene at res://scenes/creature_visuals/class_name.tscn
    /// </summary>
    public virtual string? CustomVisualPath => null;
    public virtual string? CustomTrailPath => null;
    public virtual string? CustomIconTexturePath => null; //smaller icon used in popup showing saved run info
    /// <summary>
    /// Path to a scene for top left in-run icon and compendium pool filter
    /// </summary>
    public virtual string? CustomIconPath => null;
    /// <summary>
    /// Generate icon for in-run top left and compendium pool filter.
    /// Takes precedence over CustomIconPath.
    /// </summary>
    public virtual Control? CustomIcon => null;
    /// <summary>
    /// Legacy simple energy counter API. Prefer <seealso cref="CustomEnergyCounterPath"/>CustomEnergyCounterPath.
    /// </summary>
    public virtual CustomEnergyCounter? CustomEnergyCounter => null;
    /// <summary>
    /// A pure Godot scene that BaseLib will convert into a usable NEnergyCounter at runtime.
    /// Standard Godot nodes such as Control, Label, TextureRect, Node2D, and GpuParticles2D will be converted as necessary.
    /// </summary>
    public virtual string? CustomEnergyCounterPath => null;
    
    public virtual string? CustomRestSiteAnimPath => null;
    public virtual string? CustomMerchantAnimPath => null;
    public virtual string? CustomArmPointingTexturePath => null;
    public virtual string? CustomArmRockTexturePath => null;
    public virtual string? CustomArmPaperTexturePath => null;
    public virtual string? CustomArmScissorsTexturePath => null;

    /// <summary>
    /// Override this or place your scene at res://scenes/screens/char_select/char_select_bg_class_name.tscn
    /// </summary>
    public virtual string? CustomCharacterSelectBg => null;
    public virtual string? CustomCharacterSelectIconPath => null;
    public virtual string? CustomCharacterSelectLockedIconPath => null;
    public virtual string? CustomCharacterSelectTransitionPath => null;
    public virtual string? CustomMapMarkerPath => null;
    public virtual string? CustomAttackSfx => null;
    public virtual string? CustomCastSfx => null;
    public virtual string? CustomDeathSfx => null;

    internal string? GetCustomEnergyCounterAssetPath()
    {
        if (CustomEnergyCounterPath != null) return CustomEnergyCounterPath;
        return CustomEnergyCounter != null ? SceneHelper.GetScenePath("combat/energy_counters/ironclad_energy_counter") : null;
    }

    //Defaults
    public override int StartingGold => 99;
    public override float AttackAnimDelay => 0.15f;
    public override float CastAnimDelay => 0.25f;

    protected override CharacterModel? UnlocksAfterRunAs => null;


    /// <summary>
    /// By default, will convert a scene containing the necessary nodes into a NCreatureVisuals even if it is not one.
    /// </summary>
    /// <returns></returns>
    public virtual NCreatureVisuals? CreateCustomVisuals()
    {
        if (CustomVisualPath == null) return null;
        return NodeFactory<NCreatureVisuals>.CreateFromScene(CustomVisualPath);
    }


    /// <summary>
    /// Override and return a CreatureAnimator if you need to set up states that differ from the default for your character.
    /// Using <seealso cref="SetupAnimationState"/> is suggested.
    /// </summary>
    /// <returns></returns>
    public virtual CreatureAnimator? SetupCustomAnimationStates(MegaSprite controller)
    {
        return null;
    }

    /// <summary>
    /// If you have a spine animation without all the required animations,
    /// use this method to set up a controller that will use animations of your choice for each animation.
    /// Any omitted animation parameters will default to the idle animation.
    /// </summary>
    /// <param name="controller"></param>
    /// <param name="idleName"></param>
    /// <param name="deadName"></param>
    /// <param name="deadLoop"></param>
    /// <param name="hitName"></param>
    /// <param name="hitLoop"></param>
    /// <param name="attackName"></param>
    /// <param name="attackLoop"></param>
    /// <param name="castName"></param>
    /// <param name="castLoop"></param>
    /// <param name="relaxedName"></param>
    /// <param name="relaxedLoop"></param>
    /// <returns></returns>
    public static CreatureAnimator SetupAnimationState(MegaSprite controller, string idleName, 
        string? deadName = null, bool deadLoop = false,
        string? hitName = null, bool hitLoop = false,
        string? attackName = null, bool attackLoop = false,
        string? castName = null, bool castLoop = false,
        string? relaxedName = null, bool relaxedLoop = true)
    {
        var idleAnim = new AnimState(idleName, true);
        var deadAnim = deadName == null ? idleAnim : new AnimState(deadName, deadLoop);
        var hitAnim = hitName == null ? idleAnim :
            new AnimState(hitName, hitLoop)
            {
                NextState = idleAnim
            };
        var attackAnim = attackName == null ? idleAnim :
            new AnimState(attackName, attackLoop)
            {
                NextState = idleAnim
            };
        var castAnim = castName == null ? idleAnim :
            new AnimState(castName, castLoop)
            {
                NextState = idleAnim
            };

        AnimState relaxed;
        if (relaxedName == null)
        {
            relaxed = idleAnim;
        }
        else
        {
            relaxed = new AnimState(relaxedName, relaxedLoop);
            relaxed.AddBranch("Idle", idleAnim);
        }

        var animator = new CreatureAnimator(idleAnim, controller);

        animator.AddAnyState("Idle", idleAnim);
        animator.AddAnyState("Dead", deadAnim);
        animator.AddAnyState("Hit", hitAnim);
        animator.AddAnyState("Attack", attackAnim);
        animator.AddAnyState("Cast", castAnim);
        animator.AddAnyState("Relaxed", relaxed);

        return animator;
    }
}
    
public readonly struct CustomEnergyCounter(Func<int, string> pathFunc, Color outlineColor, Color burstColor) {
    private readonly Func<int, string> _getPath = pathFunc;
    public readonly Color OutlineColor = outlineColor;
    public readonly Color BurstColor = burstColor;

    public string LayerImagePath(int layer) => _getPath(layer);
} 

[HarmonyPatch(typeof(NEnergyCounter), "OutlineColor", MethodType.Getter)]
public class EnergyCounterOutlineColorPatch {
    private static readonly FieldInfo? PlayerProp = typeof(NEnergyCounter).GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance);

    static bool Prefix(NEnergyCounter __instance, ref Color __result) {
        if (PlayerProp?.GetValue(__instance) is Player player && player.Character is CustomCharacterModel model && model.CustomEnergyCounter is CustomEnergyCounter counter) {
            __result = counter.OutlineColor;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(NEnergyCounter), nameof(NEnergyCounter.Create))]
class EnergyCounterPatch {
    private static readonly FieldInfo? PlayerField = AccessTools.Field(typeof(NEnergyCounter), "_player");
    
    [HarmonyPrefix]
    static bool Prefix(Player player, ref NEnergyCounter? __result) {
        if (player.Character is not CustomCharacterModel model)
            return true;
        
        try
        {
            if (model.CustomEnergyCounter is { } counter)
            {
                __result = NodeFactory<NEnergyCounter>.CreateFromResource(counter);
                PlayerField?.SetValue(__result, player);
                return false;
            }
            
            if (model.CustomEnergyCounterPath != null)
            {
                __result = NodeFactory<NEnergyCounter>.CreateFromScene(model.CustomEnergyCounterPath);
                PlayerField?.SetValue(__result, player);
                return false;
            }
        }
        catch (Exception e)
        {
            BaseLibMain.Logger.Error($"Failed to create custom energy counter for {player.Character.Id}: {e}");
        }

        BaseLibMain.Logger.Info($"Player {model.GetType().Name} does not have a custom NEnergyCounter.");

        return true;
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
class EnergyCounterStarAnchorPatch
{
    private static readonly FieldInfo? EnergyCounterField = AccessTools.Field(typeof(NCombatUi), "_energyCounter");
    private static readonly FieldInfo? StarCounterField = AccessTools.Field(typeof(NCombatUi), "_starCounter");

    //Allows custom energy counters to control the position of the star counter.
    [HarmonyPostfix]
    static void Postfix(NCombatUi __instance, CombatState state)
    {
        if (EnergyCounterField?.GetValue(__instance) is not NEnergyCounter energyCounter) return;
        if (StarCounterField?.GetValue(__instance) is not NStarCounter starCounter) return;
        if (energyCounter.GetNodeOrNull<CanvasItem>("%StarAnchor") is not { } starAnchor) return;

        Vector2 currentScale = starCounter.Scale;
        Vector2 targetSize = starCounter.Size == Vector2.Zero ? new Vector2(128f, 128f) : starCounter.Size;

        starCounter.Reparent(starAnchor);
        starCounter.AnchorLeft = 0f;
        starCounter.AnchorTop = 0f;
        starCounter.AnchorRight = 0f;
        starCounter.AnchorBottom = 0f;
        starCounter.OffsetLeft = 0f;
        starCounter.OffsetTop = 0f;
        starCounter.OffsetRight = targetSize.X;
        starCounter.OffsetBottom = targetSize.Y;
        starCounter.Position = Vector2.Zero;
        starCounter.Scale = currentScale;
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllCharacters), MethodType.Getter)]
public class ModelDbCustomCharacters
{
    public static readonly List<CustomCharacterModel> CustomCharacters = [];

    [HarmonyPostfix]
    public static IEnumerable<CharacterModel> AddCustomPools(IEnumerable<CharacterModel> __result)
    {
        EnsureScenePathsRegistered();
        return [.. __result, .. CustomCharacters];
    }

    public static void Register(CustomCharacterModel character)
    {
        CustomCharacters.Add(character);
    }

    /// <summary>
    /// Registers scene paths for all custom characters that haven't been registered yet.
    /// Called lazily (from AddCustomPools) rather than from the constructor, because
    /// virtual properties like CustomVisualPath may depend on fields set in derived
    /// constructors that haven't run yet when Register() is called from the base ctor.
    /// </summary>
    /// <remarks>
    /// Tracks the last-registered paths per character so that re-registration after a
    /// path change correctly unregisters the stale mapping first.
    /// </remarks>
    private static readonly Dictionary<CustomCharacterModel, (string? visualPath, string? energyPath)> _registeredPaths = [];

    internal static void EnsureScenePathsRegistered()
    {
        foreach (var character in CustomCharacters)
        {
            var visualPath = character.CustomVisualPath;
            var energyPath = character.GetCustomEnergyCounterAssetPath();

            if (_registeredPaths.TryGetValue(character, out var prev))
            {
                // Already registered — check if paths changed (e.g. mod update / hot reload)
                if (prev.visualPath == visualPath && prev.energyPath == energyPath)
                    continue;

                // Unregister stale paths before re-registering
                UnregisterCharacterPaths(prev.visualPath, prev.energyPath);
            }

            if (visualPath != null)
                NodeFactory.RegisterSceneType<NCreatureVisuals>(visualPath);
            if (energyPath != null)
                NodeFactory.RegisterSceneType<NEnergyCounter>(energyPath);

            _registeredPaths[character] = (visualPath, energyPath);
        }
    }

    /// <summary>
    /// Unregisters all scene paths registered by custom characters.
    /// Call this during cleanup or before re-initialization.
    /// </summary>
    public static void UnregisterAllCharacterScenePaths()
    {
        foreach (var (visualPath, energyPath) in _registeredPaths.Values)
            UnregisterCharacterPaths(visualPath, energyPath);

        _registeredPaths.Clear();
    }

    private static void UnregisterCharacterPaths(string? visualPath, string? energyPath)
    {
        if (visualPath != null)
            NodeFactory.UnregisterSceneType(visualPath);
        if (energyPath != null)
            NodeFactory.UnregisterSceneType(energyPath);
    }
}


[HarmonyPatch(typeof(CharacterModel), "VisualsPath", MethodType.Getter)]
class CustomCharacterVisualPath
{
    [HarmonyPrefix]
    static bool UseCustomScene(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar) return true;

        __result = customChar.CustomVisualPath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CreateVisuals))]
class CustomCharacterVisuals
{
    [HarmonyPrefix]
    static bool UseCustomVisuals(CharacterModel __instance, ref NCreatureVisuals? __result)
    {
        if (__instance is not CustomCharacterModel customChar) return true;

        __result = customChar.CreateCustomVisuals();
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.GenerateAnimator))]
class GenerateAnimatorPatch
{
    [HarmonyPrefix]
    static bool CustomAnimator(CharacterModel __instance, MegaSprite controller, ref CreatureAnimator? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.SetupCustomAnimationStates(controller);
        return __result == null;
    }
}

//Properties that require specific scenes to exist named by class ID
[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.TrailPath), MethodType.Getter)]
class TrailPath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomTrailPath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "IconTexturePath", MethodType.Getter)]
class IconTexturePath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomIconTexturePath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "Icon", MethodType.Getter)]
class Icon
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref Control? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomIcon;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "IconPath", MethodType.Getter)]
class IconPath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomIconPath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "EnergyCounterPath", MethodType.Getter)]
class EnergyCounterPath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.GetCustomEnergyCounterAssetPath();
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "RestSiteAnimPath", MethodType.Getter)]
class RestSiteAnimPath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomRestSiteAnimPath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "MerchantAnimPath", MethodType.Getter)]
class MerchantAnimPath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomMerchantAnimPath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "ArmPointingTexturePath", MethodType.Getter)]
class ArmPointingTexturePath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomArmPointingTexturePath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "ArmRockTexturePath", MethodType.Getter)]
class ArmRockTexturePath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomArmRockTexturePath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "ArmPaperTexturePath", MethodType.Getter)]
class ArmPaperTexturePath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomArmPaperTexturePath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "ArmScissorsTexturePath", MethodType.Getter)]
class ArmScissorsTexturePath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomArmScissorsTexturePath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "CharacterSelectTransitionPath", MethodType.Getter)]
class CharacterSelectTransitionPath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomCharacterSelectTransitionPath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), nameof(CharacterModel.CharacterSelectBg), MethodType.Getter)]
class CustomCharacterSelectBg
{
    [HarmonyPrefix]
    static bool UseCustomScene(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar) return true;

        __result = customChar.CustomCharacterSelectBg;
        return __result == null;
    }
}


[HarmonyPatch(typeof(CharacterModel), "CharacterSelectIconPath", MethodType.Getter)]
class CharacterSelectIconPath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomCharacterSelectIconPath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "CharacterSelectLockedIconPath", MethodType.Getter)]
class CharacterSelectLockedIconPath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomCharacterSelectLockedIconPath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "MapMarkerPath", MethodType.Getter)]
class MapMarkerPath
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomMapMarkerPath;
        return __result == null;
    }
}



[HarmonyPatch(typeof(CharacterModel), "AttackSfx", MethodType.Getter)]
class AttackSfx
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomAttackSfx;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "CastSfx", MethodType.Getter)]
class CastSfx
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomCastSfx;
        return __result == null;
    }
}

[HarmonyPatch(typeof(CharacterModel), "DeathSfx", MethodType.Getter)]
class DeathSfx
{
    [HarmonyPrefix]
    static bool Custom(CharacterModel __instance, ref string? __result)
    {
        if (__instance is not CustomCharacterModel customChar)
            return true;

        __result = customChar.CustomDeathSfx;
        return __result == null;
    }
}