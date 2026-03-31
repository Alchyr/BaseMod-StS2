using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Localization;

[HarmonyPatch(typeof(PowerModel), "AddDumbVariablesToDescription")]
class TemporaryPowerModelPatch
{
    [HarmonyPostfix]
    static void Postfix(PowerModel __instance, LocString description)
    {
        if (__instance is not CustomTemporaryPowerModel customTemporaryPowerModel)
            return;
        description.Add("TemporaryPowerTitle", customTemporaryPowerModel.InternallyAppliedPower.Title);
    }
}