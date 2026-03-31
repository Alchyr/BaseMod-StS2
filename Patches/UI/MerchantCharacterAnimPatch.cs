using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace BaseLib.Patches.UI;

[HarmonyPatch(typeof(NMerchantCharacter), nameof(NMerchantCharacter.PlayAnimation))]
class MerchantCharacterAnimPatch
{
    [HarmonyPrefix]
    public static bool SkipAnimIfNotSpine(NMerchantCharacter __instance, string anim, bool loop)
    {
        var children = __instance.GetChildren();
        if (children.Count == 0) return false;

        var targetChild = children[0];
        if (targetChild.GetType().Name.Equals(MegaSprite.spineClassName)) return true;

        return CustomAnimation.PlayCustomAnimation(__instance, anim);
    }
}