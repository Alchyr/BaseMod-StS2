using System.Reflection;
using BaseLib.Utils.NodeFactories;
using Godot;
using HarmonyLib;

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
static class InstantiatePatch
{
    static MethodBase TargetMethod()
    {
        // Explicitly pick the non-generic Instantiate(GenEditState), not Instantiate<T>(GenEditState)
        var method = typeof(PackedScene).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "Instantiate"
                        && !m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(PackedScene.GenEditState));

        if (method == null)
            throw new InvalidOperationException(
                "Could not find PackedScene.Instantiate(GenEditState). " +
                "The Godot API may have changed — auto-conversion will not work.");

        return method;
    }

    [HarmonyPostfix]
    static void Postfix(PackedScene __instance, ref Node __result)
    {
        NodeFactory.TryAutoConvert(__instance, ref __result);
    }
}
