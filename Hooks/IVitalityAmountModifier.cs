using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace TestMod.TestModCode.Hooks;

public interface IVitalityAmountModifier
{
    /// <summary>
    /// Return the amount to add.
    /// </summary>
    /// <param name="creature"></param>
    /// <param name="amount"></param>
    /// <param name="cardSource"></param>
    /// <param name="cardPlay"></param>
    /// <returns></returns>
    decimal ModifyVitalityAdditive(Creature creature, decimal amount, CardModel cardSource, CardPlay? cardPlay) => 0m;
    /// <summary>
    /// Return the amount to multiply by.
    /// </summary>
    /// <param name="creature"></param>
    /// <param name="amount"></param>
    /// <param name="cardSource"></param>
    /// <param name="cardPlay"></param>
    /// <returns></returns>
    decimal ModifyVitalityMultiplicative(Creature creature, decimal amount, CardModel cardSource, CardPlay? cardPlay) => 1m;
}