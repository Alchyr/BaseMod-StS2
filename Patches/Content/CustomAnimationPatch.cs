using Godot;
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

        if (FindNode<AnimationPlayer>(visualNodeRoot)?.UseAnimationPlayer(animName, trigger) != null)
            return false;
        if (FindNode<AnimatedSprite2D>(visualNodeRoot)?.UseAnimatedSprite2D(animName, trigger) != null)
            return false;

        if (SearchRecursive<AnimationPlayer>(visualNodeRoot)?.UseAnimationPlayer(animName, trigger) != null)
            return false;
        if (SearchRecursive<AnimatedSprite2D>(visualNodeRoot)?.UseAnimatedSprite2D(animName, trigger) != null)
            return false;
            
        return true;
    }

    private static AnimatedSprite2D UseAnimatedSprite2D(this AnimatedSprite2D animSprite, string animName, string trigger)
    {
        if (animSprite.SpriteFrames.HasAnimation(animName))
            animSprite.Play(animName);
        else if (animSprite.SpriteFrames.HasAnimation(trigger))
            animSprite.Play(trigger);
        return animSprite;
    }

    private static AnimationPlayer UseAnimationPlayer(this AnimationPlayer animPlayer, string animName, string trigger)
    {
        if (animPlayer.CurrentAnimation.Equals(animName) || animPlayer.CurrentAnimation.Equals(trigger))
            animPlayer.Stop();

        if (animPlayer.HasAnimation(animName))
            animPlayer.Play(animName);
        else if (animPlayer.HasAnimation(trigger))
            animPlayer.Play(trigger);

        return animPlayer;
    }

    private static T? FindNode<T>(Node root, string? name = null) where T : Node?
    {
        name ??= nameof(T);
        var n = root.GetNodeOrNull(name)
                ?? root.GetNodeOrNull("Visuals/" + name)
                ?? root.GetNodeOrNull("Body/" + name);
        return n as T;
    }

    private static T? SearchRecursive<T>(Node parent) where T : Node?
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is T nodeToFind) return nodeToFind;
            var found = SearchRecursive<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}