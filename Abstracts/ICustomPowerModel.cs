
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Abstracts;

public interface ICustomPowerModel : ICustomModel
{
    /*public string PackedIconPath => ImageHelper.GetImagePath("atlases/power_atlas.sprites/" + base.Id.Entry.ToLower() + ".tres"); //64x64

    private string BigIconPath => ImageHelper.GetImagePath("powers/" + base.Id.Entry.ToLower() + ".png"); //256x256

    private string BigBetaIconPath => ImageHelper.GetImagePath("powers/beta/" + base.Id.Entry.ToLower() + ".png"); //256x256
    using png for all of these is fine even though original is tres.
    since we can't reallllly use atlases properly, just using png for all is probably best.
     */


    string? CustomPackedIconPath => null;
    string? CustomBigIconPath => null;
    string? CustomBigBetaIconPath => null;
}

[HarmonyPatch(typeof(PowerModel), "PackedIconPath", MethodType.Getter)]
class PackedIconPath
{
    [HarmonyPrefix]
    static bool Custom(PowerModel __instance, ref string? __result)
    {
        if (__instance is not ICustomPowerModel customPower)
            return true;

        __result = customPower.CustomPackedIconPath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(PowerModel), "BigIconPath", MethodType.Getter)]
class BigIconPath
{
    [HarmonyPrefix]
    static bool Custom(PowerModel __instance, ref string? __result)
    {
        if (__instance is not ICustomPowerModel customPower)
            return true;

        __result = customPower.CustomBigIconPath;
        return __result == null;
    }
}

[HarmonyPatch(typeof(PowerModel), "BigBetaIconPath", MethodType.Getter)]
class BigBetaIconPath
{
    [HarmonyPrefix]
    static bool Custom(PowerModel __instance, ref string? __result)
    {
        if (__instance is not ICustomPowerModel customPower)
            return true;

        __result = customPower.CustomBigBetaIconPath;
        return __result == null;
    }
}
