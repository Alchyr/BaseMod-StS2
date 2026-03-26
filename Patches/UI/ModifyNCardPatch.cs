using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Pooling;

namespace BaseLib.Patches.UI;

public class ModifyNCardPatch
{
    /// <summary>
    /// Applies to any CardModel. Allows you to modify the original UI nodes of the card.
    /// All changes will be automatically reverted when switching to another model.
    /// </summary>
    public interface IModifyNCard
    {
        /// <summary>
        /// If any node properties that are not automatically reset are modified, card node pool should be disabled.
        /// Otherwise, it may affect other card model.
        /// <returns>Return false to disable the NCard use node pool</returns>
        /// </summary>
        bool AllowNodePool => false;
        /// <summary>
        /// Called after NCard is reloaded. You can freely modify the nodes under Body at this point.
        /// All modifications will dispose when switching to another CardModel.
        /// </summary>
        /// <param name="nCard">The NCard node instance attached to this CardModel.</param>
        void OnReload(NCard nCard);
    }
    [HarmonyPatch(typeof(NCard),"Reload")]
    public static class ReloadPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCard __instance)
        {
            if (!__instance.IsNodeReady())
            {
                return;
            }
            if (__instance.Model is IModifyNCard nCardCreate)
            {
                nCardCreate.OnReload(__instance);
            }
        }
    }
    //-----------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(GodotTreeExtensions),nameof(GodotTreeExtensions.QueueFreeSafely))]
    public static class QueueFreeSafelyPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Godot.Node node)
        {
            if (node is NCard { Model: IModifyNCard { AllowNodePool: false } })
            {
                node.QueueFreeSafelyNoPool();
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(NCard),nameof(NCard.Model), MethodType.Setter)]
    public static class NCardModelSetPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(NCard __instance,ref CardModel ____model,CardModel value)
        {
            if (____model!=value&&____model is IModifyNCard modify)
            {
                try
                {
                    NCard nc=NodePool.Get<NCard>();
                    if (nc.Body==null)
                    {
                        nc._Ready();
                    }
                    Control control = nc.Body;
                    Vector2 t=__instance.Body.Position;
                    __instance.Body.Free();
                    control.Reparent(__instance);
                    control.Position = t;
                    ____model = nc.Model;
                    nc.QueueFreeSafelyNoPool();
                    SetUniqueNameToOwner(control, __instance);
                    Info.Invoke(__instance,null);
                }
                catch (Exception e)
                {
                    MainFile.Logger.Info(e.ToString());
                }
            }
            return true;
        }
    }

    private static readonly MethodInfo Info = AccessTools.Method(typeof(NCard), nameof(NCard._Ready));

    private static void SetUniqueNameToOwner(Godot.Node node, Godot.Node parent)
    {
        node.UniqueNameInOwner = true;
        node.Owner = parent;
        foreach (Godot.Node child in node.GetChildren())
        {
            SetUniqueNameToOwner(child, parent);
        }
    }
}