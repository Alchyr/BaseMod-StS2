using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace BaseLib.Hooks;

internal static class ModCardHandOutlinePatchHelper
{
    internal static bool TryGetRule(NHandCardHolder holder, out CardModel model, out ModCardHandOutlineRule rule)
    {
        model = null!;
        rule = default;

        if (!holder.IsNodeReady() || holder.CardNode?.Model is not { } m)
            return false;

        var evaluated = ModCardHandOutlineRegistry.EvaluateBest(m);
        if (evaluated is not { } r)
            return false;

        model = m;
        rule = r;
        return true;
    }

    internal static void ApplyHighlight(NHandCardHolder holder, CardModel model, ModCardHandOutlineRule rule)
    {
        if (CombatManager.Instance is not { IsInProgress: true })
            return;

        var vanillaShow = model.CanPlay() || model.ShouldGlowRed || model.ShouldGlowGold;
        var force = rule.VisibleWhenUnplayable && !vanillaShow;
        if (!vanillaShow && !force)
            return;

        var highlight = holder.CardNode!.CardHighlight;
        if (force)
            highlight.AnimShow();

        highlight.Modulate = rule.ResolveColor(model);
    }

    internal static void ApplyFlash(NHandCardHolder holder, CardModel model, ModCardHandOutlineRule rule)
    {
        if (AccessTools.Field(typeof(NHandCardHolder), "_flash")?.GetValue(holder) is not Control flash ||
            !GodotObject.IsInstanceValid(flash))
            return;

        flash.Modulate = rule.ResolveColor(model);
    }
}