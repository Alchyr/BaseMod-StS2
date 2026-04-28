using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;

namespace BaseLib.Patches.Saves;



[HarmonyPatch(typeof(CloudSaveStore))]
public static class ModdedSaveStorePatch
{
    private static bool _isInternal;
    
    [HarmonyPostfix]
    [HarmonyPatch(nameof(CloudSaveStore.WriteFile), new[] { typeof(string), typeof(string) })]
    static void PostfixSyncString(CloudSaveStore __instance, string path) 
        => ProcessTrigger(__instance, path);
    
    [HarmonyPostfix]
    [HarmonyPatch(nameof(CloudSaveStore.WriteFile), new[] { typeof(string), typeof(byte[]) })]
    static void PostfixSyncBytes(CloudSaveStore __instance, string path) 
        => ProcessTrigger(__instance, path);
    
    [HarmonyPostfix]
    [HarmonyPatch(nameof(CloudSaveStore.WriteFileAsync), new[] { typeof(string), typeof(string) })]
    static void PostfixAsyncString(CloudSaveStore __instance, string path) 
        => ProcessTrigger(__instance, path);
    
    
    [HarmonyPostfix]
    [HarmonyPatch(nameof(CloudSaveStore.WriteFileAsync), new[] { typeof(string), typeof(byte[]) })]
    static void PostfixAsyncBytes(CloudSaveStore __instance, string path) 
        => ProcessTrigger(__instance, path);
    
    static void ProcessTrigger(CloudSaveStore __instance, string path)
    {
        var isRunSave = path.EndsWith("current_run.save") || path.EndsWith("current_run_mp.save");
        if (_isInternal || !isRunSave) return;

        _isInternal = true;
        try
        {
            foreach (var mod in ModManager.GetLoadedMods())
            {
                var modId = ModSaveUtils.GetModId(mod);
                var modPath = ModSaveUtils.GetModPath(path, modId);
                var modData = ModSaveUtils.GetModDataToSave(mod); 
                if (!string.IsNullOrEmpty(modData) && !modData.Equals("{}"))
                {
                    __instance.WriteFile(modPath, modData);
                }
            }
        }
        finally 
        { 
            _isInternal = false; 
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CloudSaveStore.ReadFile), new[] { typeof(string) })]
    static void PostfixReadFile(CloudSaveStore __instance, string path, ref string __result)
    {
        if (IsInvalidRead(path, __result)) return;
        ProcessRead(__instance, path);
    }
    
    [HarmonyPostfix]
    [HarmonyPatch(nameof(CloudSaveStore.ReadFileAsync), new[] { typeof(string) })]
    static async Task<string?> PostfixReadFileAsync(Task<string?> __result, CloudSaveStore __instance, string path)
    {
        var content = await __result;
        if (!IsInvalidRead(path, content))
        {
            ProcessRead(__instance, path);
        }
        return content;
    }
    
    private static void ProcessRead(CloudSaveStore store, string vanillaPath)
    {
        foreach (var mod in ModManager.GetLoadedMods())
        {
            var modPath = ModSaveUtils.GetModPath(vanillaPath, ModSaveUtils.GetModId(mod));

            if (!store.FileExists(modPath)) continue;
            var modJson = store.ReadFile(modPath);
            if (!string.IsNullOrEmpty(modJson))
            {
                ModSaveUtils.LoadDataIntoMod(mod, modJson);
            }
        }
    }

    private static bool IsInvalidRead(string path, string? content) => 
        string.IsNullOrEmpty(content) || 
        (!path.EndsWith("current_run.save") && !path.EndsWith("current_run_mp.save"));
    
}
