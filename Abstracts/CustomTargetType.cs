
using BaseLib.Patches.Content;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace BaseLib.Abstracts;


/// <summary>
/// Provides extended <see cref="TargetType"/> definitions to support additional targetting options.
/// </summary>
public class CustomTargetType
{
    /// <summary>
    /// Represents a multi-target selection that allows the player to target all living creature on the board.
    /// This selection is visual-only, similar to other multi-target selection types.
    /// </summary>
    [CustomEnum] public static TargetType Everyone;
    /// <summary>
    /// Represents a single-target selection that allows the player to target any living creature on the board,
    /// ignoring the standard restrictions between friendly and hostile sides.
    /// </summary>
    [CustomEnum] public static TargetType Anyone;
}


// Everyone

/// <summary>
/// Ensures that cards with the 'Everyone' target type trigger the multi-select visual 
/// state, displaying targeting reticles over all creatures in the combat room.
/// </summary>
[HarmonyPatch(typeof(NCardPlay), "ShowMultiCreatureTargetingVisuals")]
class ShowMultiCreatureTargetingVisualsPatch
{
    public static void Postfix(NCardPlay __instance)
    {
        if (__instance.Card == null || __instance.Card.TargetType != CustomTargetType.Everyone) return;
        __instance.CardNode?.UpdateVisuals(
            __instance.Card.Pile!.Type, 
            CardPreviewMode.MultiCreatureTargeting
        );

        var room = NCombatRoom.Instance;
        if (room == null) return;
        foreach (var creatureNode in room.CreatureNodes)
        {
            creatureNode.ShowMultiselectReticle();
        }
    }
}

// Anyone

/// <summary>
/// Redirects mouse-based card targeting when the 'Anyone' target type is detected.
/// </summary>
[HarmonyPatch(typeof(NMouseCardPlay), "TargetSelection")]
class TargetSelectionPatch
{
    public static bool Prefix(NMouseCardPlay __instance, TargetMode targetMode, ref Task __result)
    {
        if (__instance.Card == null || __instance.Card.TargetType != CustomTargetType.Anyone) return true;
        __result = AnyoneTargetSelectionAsync(__instance, targetMode);
        return false;

    }

    private static async Task AnyoneTargetSelectionAsync(NMouseCardPlay __instance, TargetMode targetMode)
    {
        __instance.TryShowEvokingOrbs();
        __instance.CardNode?.CardHighlight.AnimFlash();
        await __instance.SingleCreatureTargeting(targetMode, CustomTargetType.Anyone);
    }
}

// Todo: controller anyone support
/*
[HarmonyPatch(typeof(NControllerCardPlay), nameof(NControllerCardPlay.Start))]
 class ControllerStartPatch
{
    public static bool Prefix(NControllerCardPlay __instance)
    {
        var card = __instance.Card;
        if (card == null || __instance.CardNode == null || card.TargetType != CustomTarget.Anyone) return true;
        NDebugAudioManager.Instance?.Play("card_select.mp3");
        NHoverTipSet.Remove(__instance.Holder);
        UnplayableReason reason;
        AbstractModel preventer;
        if (!card.CanPlay(out reason, out preventer))
        {
            __instance.CannotPlayThisCardFtueCheck(card);
            __instance.CancelPlayCard();
            var playerDialogueLine = reason.GetPlayerDialogueLine(preventer);
            if (playerDialogueLine == null)
                return false;
            NCombatRoom.Instance.CombatVfxContainer.AddChildSafely(NThoughtBubbleVfx.Create(playerDialogueLine.GetFormattedText(), __instance.Card.Owner.Creature, 1.0));
            return false;
        }
        __instance.TryShowEvokingOrbs();
        __instance.CardNode.CardHighlight.AnimFlash();
        __instance.CenterCard();
        TaskHelper.RunSafely(__instance.SingleCreatureTargeting(card.TargetType));
        return false;
    }
}
*/


/// <summary>
/// Extends the game's target identification logic to recognize 'Anyone' as a valid 
/// single-target type.
/// </summary>
[HarmonyPatch(typeof(ActionTargetExtensions), nameof(ActionTargetExtensions.IsSingleTarget))]
class IsSingleTargetPatch
{
    public static void Postfix(TargetType targetType, ref bool __result)
    {
        if (__result) return;
        if (targetType == CustomTargetType.Anyone)
        {
            __result = true;
        }
    }
}

/// <summary>
/// Overrides the validation logic for individual creature targeting, allowing the 
/// 'Anyone' type to select any living creature regardless of faction (Ally or Enemy).
/// </summary>
[HarmonyPatch(typeof(NTargetManager), nameof(NTargetManager.AllowedToTargetCreature))]
class AllowedToTargetCreaturePatch
{
    public static bool Prefix(NTargetManager __instance, Creature creature, ref bool __result)
    {
        if (__instance._validTargetsType != CustomTargetType.Anyone) return true;
        __result = creature is { IsAlive: true };
        return false;
    }
}


// TODO: dont re-implement, instead circumvent the returns in TryPlayCard
/// <summary>
/// Re-implements the card-playing execution loop for 'Anyone' targeting, ensuring 
/// the selected creature is correctly passed to the card's play action.
/// </summary>
[HarmonyPatch(typeof(NCardPlay), nameof(NCardPlay.TryPlayCard))]
class TryPlayCardPatch
{
    public static bool Prefix(NCardPlay __instance, Creature? target)
    {
        var card = __instance.Card;
        if (card == null || card.TargetType != CustomTargetType.Anyone) return true;
        if (target == null || __instance.Holder.CardModel == null)
        {
            __instance.CancelPlayCard();
            return false;
        }
        if (!__instance.Holder.CardModel.CanPlayTargeting(target))
        {
            __instance.CannotPlayThisCardFtueCheck(__instance.Holder.CardModel);
            __instance.CancelPlayCard();
            return false;
        }
        __instance._isTryingToPlayCard = true;
        var success = card.TryManualPlay(target);
        __instance._isTryingToPlayCard = false;

        if (success)
        {
            __instance.AutoDisableCannotPlayCardFtueCheck();
            if (__instance.Holder.IsInsideTree())
            {
                var size = __instance.GetViewport().GetVisibleRect().Size;
                __instance.Holder.SetTargetPosition(new Vector2(size.X / 2f, size.Y - __instance.Holder.Size.Y));
            }
            AccessTools.Method(typeof(NCardPlay), "Cleanup").Invoke(__instance, [true]);
            var instance = NCombatRoom.Instance;
            if (instance == null)
                return false;
            instance.Ui.Hand.TryGrabFocus();
        }
        else
        {
            __instance.CancelPlayCard();
        }

        return false;
    }
}

/// <summary>
/// Patches the targeting selection logic to recognize the 'Anyone' target type.
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.CanPlayTargeting))]
class CanPlayTargetingPatch
{
    public static bool Prefix(CardModel __instance, Creature? target, ref bool __result)
    {
        if (__instance.TargetType != CustomTargetType.Anyone) return true;
        __result = target is { IsAlive: true };
        return false;
    }
}

/// <summary>
/// Overrides the card model's internal validation to ensure that any living creature 
/// is recognized as a legitimate target for cards using the 'Anyone' targeting type.
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsValidTarget))]
class IsValidTargetPatch
{
    public static bool Prefix(CardModel __instance, Creature? target, ref bool __result)
    {
        if (__instance.TargetType != CustomTargetType.Anyone) return true;
        __result = target is { IsAlive: true };
        return false;

    }
}
