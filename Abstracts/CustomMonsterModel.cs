using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Abstracts;

public abstract class CustomMonsterModel : MonsterModel, ICustomModel
{
    /// <summary>
    /// Override this or place your scene at res://scenes/creature_visuals/class_name.tscn
    /// </summary>
    public virtual string? CustomVisualPath => null;
    
    public virtual string? CustomAttackSfx => null;
    public virtual string? CustomCastSfx => null;
    public virtual string? CustomDeathSfx => null;
    
    
    /// <summary>
    /// By default, will convert a scene containing the necessary nodes into a NCreatureVisuals even if it is not one.
    /// </summary>
    /// <returns></returns>
    public virtual NCreatureVisuals? CreateCustomVisuals() {
        string? path = (CustomVisualPath ?? VisualsPath);
        if (path == null) return null;
        return GodotUtils.CreatureVisualsFromScene(path);
    }
    
    
    /// <summary>
    /// Override and return a CreatureAnimator if you need to set up states that differ from the default for the monster.
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
    /// <returns></returns>
    public static CreatureAnimator SetupAnimationState(MegaSprite controller, string idleName, 
        string? deadName = null, bool deadLoop = false,
        string? hitName = null, bool hitLoop = false,
        string? attackName = null, bool attackLoop = false,
        string? castName = null, bool castLoop = false)
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

        var animator = new CreatureAnimator(idleAnim, controller);

        animator.AddAnyState("Idle", idleAnim);
        animator.AddAnyState("Dead", deadAnim);
        animator.AddAnyState("Hit", hitAnim);
        animator.AddAnyState("Attack", attackAnim);
        animator.AddAnyState("Cast", castAnim);

        return animator;
    }
}

[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.GenerateAnimator))]
class GenerateAnimatorPatchMonster
{
    [HarmonyPrefix]
    static bool CustomAnimator(MonsterModel __instance, MegaSprite controller, ref CreatureAnimator? __result)
    {
        if (__instance is not CustomMonsterModel customMon)
            return true;

        __result = customMon.SetupCustomAnimationStates(controller);
        return __result == null;
    }
}

[HarmonyPatch(typeof(MonsterModel), "AttackSfx", MethodType.Getter)]
class AttackSfxMonster
{
    [HarmonyPrefix]
    static bool Custom(MonsterModel __instance, ref string? __result)
    {
        if (__instance is not CustomMonsterModel customMon)
            return true;

        __result = customMon.CustomAttackSfx;
        return __result == null;
    }
}

[HarmonyPatch(typeof(MonsterModel), "CastSfx", MethodType.Getter)]
class CastSfxMonster
{
    [HarmonyPrefix]
    static bool Custom(MonsterModel __instance, ref string? __result)
    {
        if (__instance is not CustomMonsterModel customMon)
            return true;

        __result = customMon.CustomCastSfx;
        return __result == null;
    }
}

[HarmonyPatch(typeof(MonsterModel), "DeathSfx", MethodType.Getter)]
class DeathSfxMonster
{
    [HarmonyPrefix]
    static bool Custom(MonsterModel __instance, ref string? __result)
    {
        if (__instance is not CustomMonsterModel customMon)
            return true;

        __result = customMon.CustomDeathSfx;
        return __result == null;
    }
}