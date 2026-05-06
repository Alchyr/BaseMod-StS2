using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Abstracts;
using BaseLib.Utils;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Patches.UI;

[HarmonyPatch]
static class CustomAnimationPatch
{
    [HarmonyPatch(typeof(NCreature), nameof(NCreature.StartDeathAnim))] //Starts playing death animation, returns time
    [HarmonyPostfix]
    static void AdjustTime(NCreature __instance, ref float __result)
    {
        if (__instance.Entity.Player?.Character is CustomCharacterModel character)
        {
            if (CustomAnimation.HasCustomAnimation(__instance.Visuals))
            {
                __result = Math.Min(character.DeathAnimTime, 30f);
            }
        }
    }
    
    //Waits for death animation to finish playing and removes UI
    [HarmonyPatch(typeof(NCreature), nameof(NCreature.AnimDie), MethodType.Async)]
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> CustomAnimDie(ILGenerator generator, IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        return AsyncMethodCall.Create(generator, instructions, original, 
            AccessTools.Method(typeof(CustomAnimationPatch), nameof(WaitCustomAnim)), beforeState: original);
    }

    static async Task WaitCustomAnim(NCreature __instance)
    {
        if (CustomAnimation.PlayCustomAnimation(__instance.Visuals, CreatureAnimator.deathTrigger, "die"))
        {
            if (__instance.Entity.Player?.Character is CustomCharacterModel character)
            {
                await Cmd.Wait(character.DeathAnimTime);
            }
        }
    }

    //Called if creature has no spine animation or has no revive animation defined
    [HarmonyPatch(typeof(NCreature), nameof(NCreature.AnimTempRevive))]
    [HarmonyPrefix]
    static bool UseCustomReviveAnim(NCreature __instance)
    {
        if (__instance.HasSpineAnimation) return true;

        return !CustomAnimation.PlayCustomAnimation(__instance.Visuals, "revive", "Revive");
    }

    [HarmonyPatch(typeof(NCreature), nameof(NCreature.SetAnimationTrigger))]
    [HarmonyPrefix]
    static bool SendTriggerToOtherAnimators(NCreature __instance, string trigger)
    {
        if (__instance.HasSpineAnimation) return true;
            
        var animName = trigger switch
        {
            CreatureAnimator.idleTrigger => "idle",
            CreatureAnimator.attackTrigger => "attack",
            CreatureAnimator.castTrigger => "cast",
            CreatureAnimator.hitTrigger => "hurt",
            CreatureAnimator.deathTrigger => "die",
            _ => trigger.ToLowerInvariant()
        };

        var visualNodeRoot = __instance.Visuals;
            
        return !CustomAnimation.PlayCustomAnimation(visualNodeRoot, animName, trigger);
    }
}