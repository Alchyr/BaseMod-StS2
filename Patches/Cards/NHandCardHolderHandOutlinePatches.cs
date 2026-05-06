using System.Collections.Concurrent;
using BaseLib.Hooks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace BaseLib.Patches.Cards;

[HarmonyPatch(typeof(NHandCardHolder), nameof(NHandCardHolder.UpdateCard))]
internal static class NHandCardHolderUpdateCardHandOutlinePatch
{
    [HarmonyPostfix]
    public static void Postfix(NHandCardHolder __instance)
    {
        if (!ModCardHandOutlinePatchHelper.TryGetRule(__instance, out var model, out var rule))
            return;

        ModCardHandOutlinePatchHelper.ApplyHighlight(__instance, model, rule);
    }
}

[HarmonyPatch(typeof(NHandCardHolder), nameof(NHandCardHolder.Flash))]
internal static class NHandCardHolderFlashHandOutlinePatch
{
    [HarmonyPostfix]
    public static void Postfix(NHandCardHolder __instance)
    {
        if (!ModCardHandOutlinePatchHelper.TryGetRule(__instance, out var model, out var rule))
            return;

        ModCardHandOutlinePatchHelper.ApplyFlash(__instance, model, rule);
    }
}

[HarmonyPatch(typeof(NHandCardHolder), nameof(NHandCardHolder._Ready))]
internal static class NHandCardHolderDynamicOutlineReadyPatch
{
    private static readonly ConcurrentDictionary<ulong, CancellationTokenSource> TokensByHolderId = new();

    [HarmonyPostfix]
    public static void Postfix(NHandCardHolder __instance)
    {
        if (!GodotObject.IsInstanceValid(__instance) || !__instance.IsInsideTree() || __instance.GetTree() == null)
            return;

        var id = __instance.GetInstanceId();
        if (!TokensByHolderId.TryAdd(id, new CancellationTokenSource()))
            return;

        var cts = TokensByHolderId[id];
        TaskHelper.RunSafely(RunDynamicRefreshLoop(__instance, id, cts.Token));
    }

    private static async Task RunDynamicRefreshLoop(NHandCardHolder holder, ulong id, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && GodotObject.IsInstanceValid(holder))
            {
                if (!holder.IsInsideTree())
                    break;

                ModCardHandOutlineRegistry.TryRefreshDynamicOutlineForHolder(holder);
                var tree = holder.GetTree();
                if (tree == null || !GodotObject.IsInstanceValid(tree))
                    break;

                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
        finally
        {
            StopLoop(id);
        }
    }

    internal static void StopLoop(ulong id)
    {
        if (!TokensByHolderId.TryRemove(id, out var cts))
            return;

        cts.Cancel();
        cts.Dispose();
    }
}

[HarmonyPatch(typeof(NHandCardHolder), nameof(NHandCardHolder._ExitTree))]
internal static class NHandCardHolderDynamicOutlineExitTreePatch
{
    [HarmonyPrefix]
    public static void Prefix(NHandCardHolder __instance)
    {
        NHandCardHolderDynamicOutlineReadyPatch.StopLoop(__instance.GetInstanceId());
    }
}