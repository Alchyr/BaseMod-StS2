using BaseLib.Abstracts;
using BaseLib.Cards.Variables;
using BaseLib.Patches.Features;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace BaseLib;

/// <summary>
/// Handles hooks for features added by BaseLib.
/// </summary>
public class BaseLibSingleton() : CustomSingletonModel(true, true)
{
    public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
    {
        var refundAmount = cardPlay.Card.DynamicVars.TryGetValue(RefundVar.Key, out var val) ? val.IntValue : 0;
        if (refundAmount > 0 && cardPlay.Resources.EnergySpent > 0)
        {
            await PlayerCmd.GainEnergy(Math.Min(refundAmount, cardPlay.Resources.EnergySpent), cardPlay.Card.Owner);
        }

        if (PurgePatch.ShouldPurge(cardPlay.Card))
        {
            var deckCard = cardPlay.Card.DeckVersion;
            if (deckCard != null)
            {
                await CardPileCmd.RemoveFromDeck(deckCard, false);
            }
        }
    }
}