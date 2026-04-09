using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.Utils;

[HarmonyPatch(typeof(CardModel), nameof(CardModel.UpgradeInternal))]
class UpgradeInternalPatch
{
    [HarmonyTranspiler]
    static List<CodeInstruction> InsertVarUpgrade(IEnumerable<CodeInstruction> code)
    {
        return new InstructionPatcher(code)
            .Match(new CallMatcher(AccessTools.Method(typeof(CardModel), "OnUpgrade")))
            .Insert([
                CodeInstruction.LoadArgument(0),
                CodeInstruction.Call(typeof(UpgradeInternalPatch), nameof(UpgradeVars))
            ]);
    }

    static void UpgradeVars(CardModel card)
    {
        foreach (var varEntry in card.DynamicVars)
        {
            var upgradeValue = DynamicVarExtensions.DynamicVarUpgrades[varEntry.Value];
            if (upgradeValue != null)
            {
                varEntry.Value.UpgradeValueBy((decimal) upgradeValue);
            }
        }
        if (card is not ConstructedCardModel constructed) return;
        constructed.ConstructedUpgrade();
    }
}