using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace TestMod.TestModCode.Hooks;

public interface IVitalityHooks
{
    /// <summary>
    /// Called before Vitality is gained.
    /// </summary>
    /// <param name="creature"></param>
    /// <param name="amount"></param>
    /// <param name="cardSource"></param>
    /// <returns></returns>
    Task BeforeVitalityGained (Creature creature, decimal amount, CardModel cardSource) => Task.CompletedTask;
    /// <summary>
    /// Called after if Vitality was modified by Model, but before Vitality is gained.
    /// </summary>
    /// <param name="amount"></param>
    /// <param name="cardSource"></param>
    /// <param name="cardPlay"></param>
    /// <returns></returns>
    Task AfterModifyingVitalityAmount(decimal amount, CardModel cardSource, CardPlay? cardPlay) => Task.CompletedTask;
    
    /// <summary>
    /// Called after Vitality is gained.
    /// </summary>
    /// <param name="creature"></param>
    /// <param name="amount"></param>
    /// <param name="cardSource"></param>
    /// <returns></returns>
    Task AfterVitalityGained(Creature creature, decimal amount, CardModel cardSource) => Task.CompletedTask;
}