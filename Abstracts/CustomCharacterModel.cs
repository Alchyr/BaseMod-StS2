using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using System.Reflection;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using BaseLib.Utils.Patching;
using System.Reflection.Emit;

namespace BaseLib.Abstracts;

public abstract class CustomCharacterModel : CharacterModel, ICustomModel
{
    public CustomCharacterModel()
    {
        ModelDbCustomCharacters.Register(this);
    }

    /// <summary>
    /// Override this or place your scene at res://scenes/creature_visuals/class_name.tscn
    /// </summary>
    public virtual string? CustomVisualPath => null;
    public virtual string? CustomTrailPath => null;
    public virtual string? CustomIconTexturePath => null; //smaller icon used in popup showing saved run info
    public virtual string? CustomIconPath => null; //top left while in run, and also icon for compendium pool filter
    public virtual CustomEnergyCounter? CustomEnergyCounter => null;
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

    /// <summary>
    /// Godot resource root for this mod's packaged audio (e.g. <c>res://mods/my_character</c>).
    /// <see cref="ModAudioHub"/> resolves <c>audio/sfx/...</c> and <c>audio/bgm/...</c> under this path.
    /// Override in your character model, or call <see cref="ModAudioHub.Register"/> directly. Return null to opt out.
    /// </summary>
    public virtual string? CustomAudioPath => null;

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
        return GodotUtils.CreatureVisualsFromScene(CustomVisualPath);
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
    static List<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        return new InstructionPatcher(instructions)
            .Match(new InstructionMatcher()
                .ldc_i4_0()
                .opcode(OpCodes.Conv_I8)
                .callvirt(null)
                .stloc_0()
            )
            .Insert([
                CodeInstruction.LoadLocal(0),
                CodeInstruction.LoadArgument(0),
                CodeInstruction.Call(typeof(EnergyCounterPatch), nameof(ChangeIroncladEnergy)),
                CodeInstruction.StoreLocal(0),
            ]);
    }

    static NEnergyCounter ChangeIroncladEnergy(NEnergyCounter defaultCounter, Player player) {
        if (player.Character is not CustomCharacterModel model || model.CustomEnergyCounter is not CustomEnergyCounter counter)
            return defaultCounter;
        NEnergyCounter energyCounter = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath(string.Concat("combat/energy_counters/ironclad_energy_counter"))).Instantiate<NEnergyCounter>(0);
        energyCounter.GetNode<TextureRect>("%Layers/Layer1").Texture = ResourceLoader.Load<Texture2D>(counter.LayerImagePath(1));
        energyCounter.GetNode<TextureRect>("%RotationLayers/Layer2").Texture = ResourceLoader.Load<Texture2D>(counter.LayerImagePath(2));
        energyCounter.GetNode<TextureRect>("%RotationLayers/Layer3").Texture = ResourceLoader.Load<Texture2D>(counter.LayerImagePath(3));
        energyCounter.GetNode<TextureRect>("%Layers/Layer4").Texture = ResourceLoader.Load<Texture2D>(counter.LayerImagePath(4));
        energyCounter.GetNode<TextureRect>("%Layers/Layer5").Texture = ResourceLoader.Load<Texture2D>(counter.LayerImagePath(5));
        energyCounter.GetNode<CpuParticles2D>("%BurstBack").Color = counter.BurstColor;
        energyCounter.GetNode<CpuParticles2D>("%BurstFront").Color = counter.BurstColor;
        return energyCounter;
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllCharacters), MethodType.Getter)]
public class ModelDbCustomCharacters
{
    public static readonly List<CustomCharacterModel> CustomCharacters = [];

    [HarmonyPostfix]
    public static IEnumerable<CharacterModel> AddCustomPools(IEnumerable<CharacterModel> __result)
    {
        return [.. __result, .. CustomCharacters];
    }

    public static void Register(CustomCharacterModel character)
    {
        CustomCharacters.Add(character);
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

        __result = customChar.CustomEnergyCounterPath;
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