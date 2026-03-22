using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.CreateVisuals))]
class ScriptlessNCreatureVisuals
{
    [HarmonyPrefix]
    public static bool Prefix(MonsterModel __instance, ref NCreatureVisuals __result) {
        CustomMonsterModel customMonsterModel =  __instance as CustomMonsterModel;
        if (customMonsterModel != null) {
            MainFile.Logger.Info("MonsterModel is CustomMonsterModel, using CreateCustomVisuals");
            __result = customMonsterModel!.CreateCustomVisuals();
            return false;
        }
        try {
            var VisualsPath = Traverse.Create(__instance).Property<string>("VisualsPath").Value;
            NCreatureVisuals scene = PreloadManager.Cache.GetScene(VisualsPath).Instantiate<NCreatureVisuals>();
            __result = scene;
            return false;
        }
        catch (Exception e) when (e is NullReferenceException || e is InvalidCastException) {
            MainFile.Logger.Info("Visuals are not NCreatureVisuals, attempting CreateCustomVisuals");
            __result = customMonsterModel.CreateCustomVisuals();
            return false;
        }
        return true;
    }
}