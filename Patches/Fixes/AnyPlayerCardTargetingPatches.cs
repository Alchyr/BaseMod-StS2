using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using static BaseLib.Patches.Fixes.AnyPlayerCardTargetingHelper;

namespace BaseLib.Patches.Fixes;

// ── Shared helper ────────────────────────────────────────────────────────────

internal static class AnyPlayerCardTargetingHelper
{
    internal static bool IsAnyPlayerMultiplayer(CardModel? card)
    {
        return card is { TargetType: TargetType.AnyPlayer }
               && card.Owner.RunState.Players.Count > 1;
    }
}

// ── P1: CardModel.IsValidTarget ──────────────────────────────────────────────
//  Vanilla returns false for non-null AnyPlayer targets and true for null
//  AnyPlayer targets regardless of player count, breaking multiplayer targeting.

[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsValidTarget))]
internal static class CardModelIsValidTargetAnyPlayerPatch
{
    [HarmonyPrefix]
    private static bool CheckValidPlayerTarget(CardModel __instance, Creature? target, ref bool __result)
    {
        if (__instance.TargetType != TargetType.AnyPlayer)
            return true;

        if (target == null)
        {
            __result = __instance.Owner.RunState.Players.Count <= 1;
            return false;
        }

        __result = target is { IsAlive: true, IsPlayer: true };
        return false;
    }
}

// ── P2: NCardPlay.TryPlayCard ────────────────────────────────────────────────
//  Vanilla treats AnyPlayer as non-targeted, always calling TryManualPlay(null).
//  In multiplayer we need to pass the selected target through.

[HarmonyPatch(typeof(NCardPlay), "TryPlayCard")]
internal static class NCardPlayTryPlayCardAnyPlayerPatch
{
    private static readonly Action<NCardPlay, bool>? CleanupBool = CreateCleanupBool();
    private static readonly Action<NCardPlay>? CleanupVoid = CreateCleanupVoid();

    [HarmonyPrefix]
    private static bool TryPlayAnyPlayer(NCardPlay __instance, Creature? target)
    {
        var card = __instance.Card;
        if (!IsAnyPlayerMultiplayer(card))
            return true;

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
        var played = card!.TryManualPlay(target);
        __instance._isTryingToPlayCard = false;

        if (played)
        {
            __instance.AutoDisableCannotPlayCardFtueCheck();
            if (__instance.Holder.IsInsideTree())
            {
                var size = __instance.GetViewport().GetVisibleRect().Size;
                __instance.Holder.SetTargetPosition(new Vector2(size.X / 2f, size.Y - __instance.Holder.Size.Y));
            }

            InvokeCleanupFinished(__instance, true);
            NCombatRoom.Instance?.Ui.Hand.TryGrabFocus();
        }
        else
        {
            __instance.CancelPlayCard();
        }

        return false;
    }

    private static Action<NCardPlay, bool>? CreateCleanupBool()
    {
        var mi = AccessTools.DeclaredMethod(typeof(NCardPlay), "Cleanup", [typeof(bool)]);
        return mi != null ? AccessTools.MethodDelegate<Action<NCardPlay, bool>>(mi) : null;
    }

    private static Action<NCardPlay>? CreateCleanupVoid()
    {
        if (CleanupBool != null) return null;
        var mi = AccessTools.DeclaredMethod(typeof(NCardPlay), "Cleanup", Type.EmptyTypes);
        return mi != null ? AccessTools.MethodDelegate<Action<NCardPlay>>(mi) : null;
    }

    private static void InvokeCleanupFinished(NCardPlay instance, bool success)
    {
        if (CleanupBool != null)
        {
            CleanupBool(instance, success);
            return;
        }

        CleanupVoid?.Invoke(instance);
        instance.EmitSignal(NCardPlay.SignalName.Finished, success);
    }
}

// ── P3: NMouseCardPlay.TargetSelection ───────────────────────────────────────
//  Vanilla routes AnyPlayer to MultiCreatureTargeting (no arrow).
//  SingleCreatureTargeting already fully supports AnyPlayer via NTargetManager.

[HarmonyPatch(typeof(NMouseCardPlay), "TargetSelection")]
internal static class NMouseCardPlayTargetSelectionAnyPlayerPatch
{
    [HarmonyPrefix]
    private static bool AnyPlayerTargeting(NMouseCardPlay __instance, TargetMode targetMode, ref Task __result)
    {
        if (!IsAnyPlayerMultiplayer(__instance.Card))
            return true;

        __result = RunAnyPlayerTargeting(__instance, targetMode);
        return false;
    }

    private static async Task RunAnyPlayerTargeting(NMouseCardPlay instance, TargetMode targetMode)
    {
        var cardNode = instance.CardNode;
        if (cardNode == null) return;

        instance.TryShowEvokingOrbs();
        cardNode.CardHighlight.AnimFlash();
        await instance.SingleCreatureTargeting(targetMode, TargetType.AnyPlayer);
    }
}

// ── P4a: NControllerCardPlay.Start ───────────────────────────────────────────
//  Same routing issue as mouse: AnyPlayer goes to MultiCreatureTargeting.

[HarmonyPatch(typeof(NControllerCardPlay), nameof(NControllerCardPlay.Start))]
internal static class NControllerCardPlayStartAnyPlayerPatch
{
    [HarmonyPrefix]
    private static bool ControllerCardPlay(NControllerCardPlay __instance)
    {
        if (!IsAnyPlayerMultiplayer(__instance.Card))
            return true;

        var card = __instance.Card;
        var cardNode = __instance.CardNode;
        if (card == null || cardNode == null)
            return false;

        NDebugAudioManager.Instance?.Play("card_select.mp3");
        NHoverTipSet.Remove(__instance.Holder);

        if (!card.CanPlay(out var reason, out var preventer))
        {
            __instance.CannotPlayThisCardFtueCheck(card);
            __instance.CancelPlayCard();
            var line = reason.GetPlayerDialogueLine(preventer);
            if (line != null)
                NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(
                    NThoughtBubbleVfx.Create(line.GetFormattedText(), card.Owner.Creature, 1.0));
            return false;
        }

        __instance.TryShowEvokingOrbs();
        cardNode.CardHighlight.AnimFlash();
        __instance.CenterCard();
        TaskHelper.RunSafely(__instance.SingleCreatureTargeting(TargetType.AnyPlayer));

        return false;
    }
}

// ── P4b: NControllerCardPlay.SingleCreatureTargeting ─────────────────────────
//  Vanilla's switch only handles AnyEnemy/AnyAlly, leaving the candidate list
//  empty for AnyPlayer (immediately cancels).

[HarmonyPatch(typeof(NControllerCardPlay), "SingleCreatureTargeting")]
internal static class NControllerCardPlaySingleTargetingAnyPlayerPatch
{
    [HarmonyPrefix]
    private static bool ControllerTargeting(NControllerCardPlay __instance, TargetType targetType, ref Task __result)
    {
        if (targetType != TargetType.AnyPlayer)
            return true;

        __result = AnyPlayerControllerTargeting(__instance);
        return false;
    }

    private static async Task AnyPlayerControllerTargeting(NControllerCardPlay instance)
    {
        var card = instance.Card;
        if (card?.CombatState == null)
        {
            instance.CancelPlayCard();
            return;
        }

        var cardNode = instance.CardNode;
        if (cardNode == null)
        {
            instance.CancelPlayCard();
            return;
        }

        var targetManager = NTargetManager.Instance;

        var list = card.CombatState!.PlayerCreatures
            .Where(c => c is { IsAlive: true, IsPlayer: true })
            .ToList();

        if (list.Count == 0)
        {
            instance.CancelPlayCard();
            return;
        }

        var nodes = list
            .Select(c => NCombatRoom.Instance!.GetCreatureNode(c))
            .OfType<NCreature>()
            .ToList();

        if (nodes.Count == 0)
        {
            instance.CancelPlayCard();
            return;
        }

        var hoverCallable = Callable.From((NCreature c) => instance.OnCreatureHover(c));
        var unhoverCallable = Callable.From((NCreature c) => instance.OnCreatureUnhover(c));

        try
        {
            targetManager.Connect(NTargetManager.SignalName.CreatureHovered, hoverCallable);
            targetManager.Connect(NTargetManager.SignalName.CreatureUnhovered, unhoverCallable);
            targetManager.StartTargeting(
                TargetType.AnyPlayer, cardNode, TargetMode.Controller,
                () => !GodotObject.IsInstanceValid(instance)
                      || !NControllerManager.Instance!.IsUsingController,
                null);

            NCombatRoom.Instance!.RestrictControllerNavigation(nodes.Select(n => n.Hitbox));
            nodes.First().Hitbox.TryGrabFocus();

            var selected = (NCreature?)await targetManager.SelectionFinished();

            if (!GodotObject.IsInstanceValid(instance))
                return;

            if (selected != null)
                instance.TryPlayCard(selected.Entity);
            else
                instance.CancelPlayCard();
        }
        finally
        {
            if (targetManager.IsConnected(NTargetManager.SignalName.CreatureHovered, hoverCallable))
                targetManager.Disconnect(NTargetManager.SignalName.CreatureHovered, hoverCallable);

            if (targetManager.IsConnected(NTargetManager.SignalName.CreatureUnhovered, unhoverCallable))
                targetManager.Disconnect(NTargetManager.SignalName.CreatureUnhovered, unhoverCallable);
        }
    }
}

// ── P5: CardCmd.AutoPlay ─────────────────────────────────────────────────────
//  Vanilla only resolves random targets for AnyEnemy/AnyAlly.
//  This adds the same RNG fallback for AnyPlayer.

[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.AutoPlay))]
internal static class CardCmdAutoPlayAnyPlayerPatch
{
    [HarmonyPrefix]
    private static void RandomAnyPlayer(CardModel card, ref Creature? target)
    {
        if (card.TargetType != TargetType.AnyPlayer || target != null)
            return;

        var combatState = card.CombatState ?? card.Owner.Creature.CombatState;
        if (combatState == null)
            return;

        var candidates = combatState.PlayerCreatures
            .Where(c => c is { IsAlive: true, IsPlayer: true });
        target = card.Owner.RunState.Rng.CombatTargets.NextItem(candidates);
    }
}