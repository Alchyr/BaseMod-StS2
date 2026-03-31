using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace BaseLib.Abstracts;

public abstract class CustomOrbModel : OrbModel, ICustomModel, ILocalizationProvider
{
    internal static readonly List<CustomOrbModel> RegisteredOrbs = [];

    public virtual string? CustomIconPath => null;
    public virtual string? CustomSpritePath => null;

    /// <summary>
    /// Set to true to include this orb in the random orb pool (e.g. Chaos).
    /// </summary>
    public virtual bool IncludeInRandomPool => false;

    public virtual string? CustomPassiveSfx => null;
    public virtual string? CustomEvokeSfx => null;
    public virtual string? CustomChannelSfx => null;

    protected override string PassiveSfx => CustomPassiveSfx ?? base.PassiveSfx;
    protected override string EvokeSfx => CustomEvokeSfx ?? base.EvokeSfx;
    protected override string ChannelSfx => CustomChannelSfx ?? base.ChannelSfx;

    /// <summary>
    /// Override to create a custom sprite for this orb.
    /// If null is returned, falls back to CustomSpritePath or the default sprite.
    /// </summary>
    public virtual Node2D? CreateCustomSprite() => null;

    public CustomOrbModel()
    {
        RegisteredOrbs.Add(this);
    }

    /// <summary>
    /// Remove all orbs belonging to the given assembly and clear the random pool
    /// cache so it gets rebuilt on next access. Called during hot reload cleanup.
    /// </summary>
    internal static void RemoveByAssembly(System.Reflection.Assembly asm)
    {
        RegisteredOrbs.RemoveAll(o => o.GetType().Assembly == asm);
        // Force the random pool cache to rebuild with the new set of orbs
        CustomOrbRandomPool.ClearCache();
    }
    
    /// <summary>
    /// Override this to define localization directly in your class.
    /// You are recommended to return an OrbLoc<seealso cref="OrbLoc"/>.
    /// </summary>
    public virtual List<(string, string)>? Localization => null;
}

[HarmonyPatch(typeof(OrbModel), "IconPath", MethodType.Getter)]
class CustomOrbIconPath
{
    [HarmonyPrefix]
    static bool Custom(OrbModel __instance, ref string __result)
    {
        if (__instance is not CustomOrbModel custom || custom.CustomIconPath is not string path)
            return true;
        __result = path;
        return false;
    }
}

[HarmonyPatch(typeof(OrbModel), "SpritePath", MethodType.Getter)]
class CustomOrbSpritePath
{
    [HarmonyPrefix]
    static bool Custom(OrbModel __instance, ref string __result)
    {
        if (__instance is not CustomOrbModel custom || custom.CustomSpritePath is not string path)
            return true;
        __result = path;
        return false;
    }
}

[HarmonyPatch(typeof(OrbModel), nameof(OrbModel.CreateSprite))]
class CustomOrbCreateSprite
{
    [HarmonyPrefix]
    static bool Custom(OrbModel __instance, ref Node2D __result)
    {
        if (__instance is not CustomOrbModel custom)
            return true;

        var sprite = custom.CreateCustomSprite();
        if (sprite == null)
            return true;

        __result = sprite;
        return false;
    }
}

[HarmonyPatch(typeof(OrbModel), nameof(OrbModel.GetRandomOrb))]
class CustomOrbRandomPool
{
    private static List<OrbModel>? _eligibleCache;

    /// <summary>Null the cache so it gets rebuilt from the current RegisteredOrbs list.</summary>
    internal static void ClearCache() => _eligibleCache = null;

    static void Postfix(Rng rng, ref OrbModel __result)
    {
        _eligibleCache ??= CustomOrbModel.RegisteredOrbs
            .Where(o => o.IncludeInRandomPool)
            .ToList<OrbModel>();

        if (_eligibleCache.Count == 0) return;

        // Roll against vanilla pool size (5) + custom count so each orb has equal odds
        const int vanillaOrbCount = 5; // Lightning, Frost, Dark, Plasma, Glass
        int totalPool = vanillaOrbCount + _eligibleCache.Count;
        int roll = rng.NextInt(totalPool);
        if (roll < _eligibleCache.Count)
            __result = _eligibleCache[roll];
    }
}
