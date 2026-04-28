using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;

namespace BaseLib.Patches.Saves;

[HarmonyPatch(typeof(SaveManager), "EnumerateCloudSyncTasks")]
static class ModdedSaveSyncPatch
{
    public static IEnumerable<Task> Postfix(IEnumerable<Task> __result, CloudSaveStore cloudStore)
    {
        foreach (var task in __result) yield return task;
        for (var i = 1; i <= 3; i++)
        {
            var profileModDir = UserDataPathProvider.GetProfileDir(i) + "/saves/mods";
            foreach (var modFolder in cloudStore.CloudStore.GetDirectoriesInDirectory(profileModDir))
            {
                yield return cloudStore.SyncCloudToLocal($"{profileModDir}/{modFolder}/current_run.save");
                yield return cloudStore.SyncCloudToLocal($"{profileModDir}/{modFolder}/current_run_mp.save");
            }
        }
    }
}