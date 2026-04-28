using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;

namespace BaseLib.Patches.Saves;

[HarmonyPatch(typeof(CloudSaveStore), "DeleteFile")]
static class ModdedSaveDeletePatch
{
    public static void Postfix(CloudSaveStore __instance, string path)
    {
        var isTargetFile = path.EndsWith("current_run.save") || 
                           path.EndsWith("current_run_mp.save") ||
                           path.EndsWith(".save.backup");
        if (!isTargetFile) return;

        foreach (var mod in ModManager.GetLoadedMods())
        {
            var modId = ModSaveUtils.GetModId(mod);
            var modPath = ModSaveUtils.GetModPath(path, modId);

            if (__instance.FileExists(modPath))
            {
                __instance.DeleteFile(modPath);
            }
        }
    }
}