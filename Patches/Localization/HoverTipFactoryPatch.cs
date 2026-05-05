using BaseLib.Patches.Content;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace BaseLib.Patches.Localization;

[HarmonyPatch(typeof(HoverTipFactory), nameof(HoverTipFactory.Static))]
public static class StaticHoverTipPatch
{
    [HarmonyPrefix]
    public static bool StaticHoverTipPrefix(StaticHoverTip tip, DynamicVar[] vars, ref IHoverTip __result)
    {
        if (!CustomEnums.GeneratedCustomEnumEntries.TryGetValue(typeof(StaticHoverTip), out var values))
            return true;
        if (!values.TryGetValue((int)tip, out var name))
            return true;

        var slugName = StringHelper.Slugify(name);
        var title = new LocString("static_hover_tips", slugName + ".title");
        var description = new LocString("static_hover_tips", slugName + ".description");
        foreach (DynamicVar var in vars)
        {
            title.Add(var);
            description.Add(var);
        }
        __result = new HoverTip(title, description);
        return false;
    }
}