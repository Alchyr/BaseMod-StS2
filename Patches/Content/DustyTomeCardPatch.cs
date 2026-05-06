using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(DustyTome), nameof(DustyTome.SetupForPlayer))]
class DustyTomePatch
{
    private static bool _initialized = false;
    private static readonly Dictionary<ModelId, List<ModelId>> _customTome = [];
    private static Dictionary<ModelId, List<ModelId>> CustomTome
    {
        get
        {
            if (_initialized) return _customTome;
            
            _initialized = true;
            int count = 0;
            foreach (var cardModel in ModelDb.AllCards)
            {
                if (cardModel is ITomeCard target)
                {
                    if (!_customTome.TryGetValue(target.TomeCharacter.Id, out var cardList))
                    {
                        cardList = [];
                        _customTome[target.TomeCharacter.Id] = cardList;
                    }
                    cardList.Add(cardModel.Id);
                    ++count;
                }
            }
            
            BaseLibMain.Logger.Info($"Initialized DustyTome dictionary; found {count} ITomeCard implementations");

            return _customTome;
        }
    }

    [HarmonyPrefix]
    static bool DustyTomeCardOverride(DustyTome __instance, Player player)
    {
        if (CustomTome.TryGetValue(player.Character.Id, out var cardList))
        {
            __instance.AncientCard = player.PlayerRng.Rewards.NextItem(cardList);
            return false;
        }

        return true;
    }
}