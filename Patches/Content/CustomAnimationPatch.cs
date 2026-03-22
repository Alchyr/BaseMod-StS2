using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Patches.Content
{
    [HarmonyPatch(typeof(NCreature), nameof(NCreature.SetAnimationTrigger))]
    class CustomAnimationPatch
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

            NCreatureVisuals visualNodeRoot = __instance.Visuals;
            
            var animPlayer = FindNode<AnimationPlayer>(visualNodeRoot);
            if (animPlayer != null) {
                UseAnimationPlayer(animPlayer, animName, trigger);
                return false;
            }
            var animSprite = FindNode<AnimatedSprite2D>(visualNodeRoot);
            if (animSprite != null) {
                UseAnimatedSprite2D(animSprite, animName, trigger);
                return false;
            }
            
            animPlayer ??= SearchRecursive<AnimationPlayer>(visualNodeRoot);
            if (animPlayer != null) {
                UseAnimationPlayer(animPlayer, animName, trigger);
                return false;
            }
            animSprite ??= SearchRecursive<AnimatedSprite2D>(visualNodeRoot);
            if (animSprite != null) {
                UseAnimatedSprite2D(animSprite, animName, trigger);
                return false;
            }
            
            return false;
        }

        private static void UseAnimatedSprite2D(AnimatedSprite2D animSprite, string animName, string trigger)
        {
            if (animSprite.SpriteFrames.HasAnimation(animName))
                animSprite.Play(animName);
            else if (animSprite.SpriteFrames.HasAnimation(trigger))
                animSprite.Play(trigger);
        }

        private static void UseAnimationPlayer(AnimationPlayer animPlayer, string animName, string trigger)
        {
            if (animPlayer.CurrentAnimation.Equals(animName) || animPlayer.CurrentAnimation.Equals(trigger))
                animPlayer.Stop();

            if (animPlayer.HasAnimation(animName))
                animPlayer.Play(animName);
            else if (animPlayer.HasAnimation(trigger))
                animPlayer.Play(trigger);
        }

        private static T? FindNode<T>(Node root, string? name = null ) where T : Node?
        {
            name = name ?? nameof(T);
            return root.GetNodeOrNull<T>(name)
                   ?? root.GetNodeOrNull<T>("Visuals/"+name)
                   ?? root.GetNodeOrNull<T>("Body/"+name);
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
}
