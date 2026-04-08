using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(ArchaicTooth), nameof(ArchaicTooth.TranscendenceUpgrades),  MethodType.Getter)]
public static class ArchaicToothTranscendenceUpgradesPatch
{
    [HarmonyPostfix]
    public static void AddTranscendenceUpgradeForCustomCharacters(ref Dictionary<ModelId, CardModel> __result)
    {
        foreach (var cardModel in ModelDb.AllCards)
        {
            if (cardModel is ICustomTranscendenceTarget target)
            {
                __result[cardModel.Id] = target.GetTranscendenceTransformedCard();
            }
        }
    }
}