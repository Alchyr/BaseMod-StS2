using System.Reflection;
using BaseLib.Abstracts;
using BaseLib.Utils.NodeFactories;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Content;

/// <summary>
/// Patches the non-generic PackedScene.Instantiate so that registered scenes
/// are auto-converted to the correct node type before Instantiate&lt;T&gt;'s castclass runs.
///
/// This makes it so modders can use standard Godot scenes (Node2D root, etc)
/// and have them transparently converted to game types like NCreatureVisuals
/// without needing per-callsite Harmony patches.
///
/// Uses TargetMethod to avoid ambiguity between the generic and non-generic overloads.
/// </summary>
[HarmonyPatch]
static class SceneConversionPatch
{
    static MethodBase TargetMethod()
    {
        // Explicitly pick the non-generic Instantiate(GenEditState), not Instantiate<T>(GenEditState)
        var method = typeof(PackedScene).GetMethod("Instantiate", 0, [typeof(PackedScene.GenEditState)]);

        if (method == null)
            throw new InvalidOperationException(
                "Could not find PackedScene.Instantiate(GenEditState). " +
                "The Godot API may have changed — auto-conversion will not work.");

        return method;
    }

    [HarmonyPostfix]
    static void Postfix(PackedScene __instance, ref Node? __result)
    {
        NodeFactory.TryAutoConvert(__instance, ref __result);
    }
}

/// <summary>
/// Registers custom scene paths.
/// Called through a patch because virtual properties like CustomVisualPath
/// may depend on fields set in derived constructors that haven't run yet when
/// the base constructor occurs.
/// </summary>
[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Preload))]
class RegisterSceneConversions
{
    [HarmonyPostfix]
    private static void EnsureScenePathsRegistered()
    {
        foreach (var type in CustomContentDictionary.RegisteredTypes)
        {
            if (!type.IsAssignableTo(typeof(ISceneConversions))) continue;
            
            var model = ModelDb.GetById<AbstractModel>(ModelDb.GetId(type));
            (model as ISceneConversions)?.RegisterSceneConversions();
        }
    }
}
