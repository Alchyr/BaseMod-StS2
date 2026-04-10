using System.Collections.Concurrent;
using System.Reflection;
using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils.Attributes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.GetEntry))]
public class PrefixIdPatch
{
    /// <summary>Types where BaseLib rewrites <c>GetEntry</c> (custom id or <see cref="ICustomModel" /> prefix).</summary>
    private static readonly ConcurrentDictionary<Type, string> TransformCache = new();

    /// <summary>Types already classified as needing no BaseLib transform (skip reflection on later calls).</summary>
    private static readonly ConcurrentDictionary<Type, byte> PassthroughTypes = new();

    [HarmonyPostfix]
    static void AdjustID(ref string __result, Type type)
    {
        if (PassthroughTypes.TryGetValue(type, out _))
            return;

        if (TransformCache.TryGetValue(type, out var cached))
        {
            __result = cached;
            return;
        }

        var attr = type.GetCustomAttribute<CustomIDAttribute>();
        if (attr != null)
        {
            TransformCache[type] = attr.ID;
            __result = attr.ID;
            return;
        }

        if (type.IsAssignableTo(typeof(ICustomModel)))
        {
            var prefixed = type.GetPrefix() + __result;
            TransformCache[type] = prefixed;
            __result = prefixed;
            return;
        }

        PassthroughTypes.TryAdd(type, 0);
    }
}
