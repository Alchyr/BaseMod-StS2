using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(NCreature), nameof(NCreature.SetAnimationTrigger))]
static class CustomAnimationPatch
{
    [HarmonyPrefix]
    public static bool Prefix(NCreature __instance, string trigger)
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