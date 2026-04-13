using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Localization;

/// <summary>
/// Adds some extra dumb variables to the Power description
/// </summary>
public interface IAddDumbVariablesToPowerDescription
{
    /// <inheritdoc cref="IAddDumbVariablesToPowerDescription"/>
    /// <param name="description">The original description</param>
    public void AddDumbVariablesToPowerDescription(LocString description);
}

[HarmonyPatch(typeof(PowerModel))]
class PowerModelLocPatch
{
    [HarmonyPatch("AddDumbVariablesToDescription")]
    [HarmonyPostfix]
    static void Postfix(PowerModel __instance, LocString description)
    {
        if (__instance is not IAddDumbVariablesToPowerDescription power)
            return;
        power.AddDumbVariablesToPowerDescription(description);
    }
}