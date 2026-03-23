using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using Godot;
using BaseLib.Config;
using BaseLib.Config.UI;
using BaseLib.Utils;

namespace BaseLib.Patches.UI;

[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer._Ready))]
public static class ModConfigButtonPatch
{
    public static readonly SpireField<NModInfoContainer, Control> ConfigButton = new(node => NConfigOpenerButton.Create("ConfigButton", node));
    
    [HarmonyPostfix]
    public static void PrepButton(NModInfoContainer __instance)
    {
        ConfigButton.Get(__instance);
    }
}

[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Fill))]
public static class ModConfigFillPatch
{
    public static Mod? CurrentMod { get; private set; }

    public static void Postfix(NModInfoContainer __instance, Mod mod)
    {
        CurrentMod = mod;
        var configButton = ModConfigButtonPatch.ConfigButton.Get(__instance);
        if (configButton != null)
        {
            if (mod.wasLoaded && mod.manifest != null && ModConfigRegistry.Get(mod.manifest.id) != null)
            {
                configButton.Show();
            }
            else
            {
                configButton.Hide();
            }
        }
    }
}
