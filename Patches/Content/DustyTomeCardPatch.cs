using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(DustyTome), nameof(DustyTome.SetupForPlayer))]
public class DarvAncientCardPatch
{
    private static Dictionary<CharacterModel, ModelId>? _customTome;

    [HarmonyPostfix]
    static void DustyTomeCardOverride(DustyTome __instance, Player player)
    {
        if (_customTome == null)
        {
            _customTome = [];
            foreach (var cardModel in ModelDb.AllCards)
            {
                if (cardModel is ITomeCard target)
                {
                    // While this could be handled as an interface of the CharacterModel, I feel it would be better done on the card.
                    _customTome[target.GetCharacterModel()] = cardModel.Id;
                }
            }
        }

        if(_customTome.TryGetValue(player.Character, out var cardId))
            __instance.AncientCard = cardId;
    }
}