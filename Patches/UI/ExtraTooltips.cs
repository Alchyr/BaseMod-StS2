using System.Collections.Generic;
using BaseLib.Extensions;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Patches.UI;

[HarmonyPatch(typeof(CardModel), "HoverTips", MethodType.Getter)]
public class ExtraTooltips
{
    [HarmonyTranspiler]
    static List<CodeInstruction> AddCustomTips(IEnumerable<CodeInstruction> instructions)
    {
        return new InstructionPatcher(instructions)
            .Match(new InstructionMatcher()
                .ldarg_0()
                .callvirt(AccessTools.PropertyGetter(typeof(CardModel), "ExtraHoverTips"))
                .call(null)
                .stloc_0()
            )
            .Insert([
                CodeInstruction.LoadLocal(0), //Load stored list
                CodeInstruction.LoadArgument(0), //Load card
                CodeInstruction.Call(typeof(ExtraTooltips), "AddTips"), //add tips to list
            ]);
    }

    public static void AddTips(List<IHoverTip> tips, CardModel card)
    {
        //dynvar tips
        foreach (DynamicVar var in card.DynamicVars.Values)
        {
            var tip = DynamicVarExtensions.DynamicVarTips[var]?.Invoke();
            if (tip != null) tips.Add(tip);
        }
    }
}

[HarmonyPatch(typeof(DynamicVar), nameof(DynamicVar.Clone))]
class CloneTooltips
{
    [HarmonyPostfix]
    static DynamicVar Copy(DynamicVar __result, DynamicVar __instance)
    {
        var tipMaker = DynamicVarExtensions.DynamicVarTips[__instance];
        if (tipMaker != null) DynamicVarExtensions.DynamicVarTips[__result] = tipMaker;

        return __result;
    }
}