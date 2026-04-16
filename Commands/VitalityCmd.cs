using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using TestMod.TestModCode.Hooks;
using TestMod.TestModCode.Patches;

namespace TestMod.TestModCode.Commands;

public class VitalityCmd
{
    public static async Task<Decimal> GainVitality(
        Creature creature,
        Decimal amount,
        CardPlay? cardPlay,
        bool fast = false)
    {
        if (CombatManager.Instance.IsOverOrEnding)
            return 0M;
        CombatState combatState = creature.CombatState;
        await BeforeVitalityGained(combatState, creature, amount, cardPlay?.Card);
        Decimal modifiedAmount = amount;
        IEnumerable<AbstractModel> modifiers;
        modifiedAmount = ModifyVitality(combatState, creature, modifiedAmount, cardPlay.Card, cardPlay, out modifiers);
        modifiedAmount = Math.Max(modifiedAmount, 0M);
        await AfterModifyingVitalityAmount(combatState, modifiedAmount, cardPlay?.Card, cardPlay, modifiers);
        if (modifiedAmount > 0M)
        {
            SfxCmd.Play("event:/sfx/heal");
            VfxCmd.PlayOnCreatureCenter(creature, "vfx/vfx_cross_heal");
            VitalityPatch.VitalityField.SetVitality(creature, (int) amount + VitalityPatch.VitalityField.GetVitality(creature));
            CombatManager.Instance.History.Add(new VitalityGainedEntry((int)modifiedAmount, cardPlay, creature, combatState.RoundNumber, combatState.CurrentSide, CombatManager.Instance.History));
            if (fast)
                await Cmd.CustomScaledWait(0.0f, 0.03f);
            else
                await Cmd.CustomScaledWait(0.1f, 0.25f);
        }
        await AfterVitalityGained(combatState, creature, modifiedAmount, cardPlay?.Card);
        return modifiedAmount;
    }
    
    static decimal ModifyVitality(
        CombatState combatState,
        Creature creature,
        Decimal amount,
        CardModel? cardSource,
        CardPlay? cardPlay,
        out IEnumerable<AbstractModel> modifiers)
    {
        decimal num = amount;
        List<AbstractModel> abstractModelList = new List<AbstractModel>();
        
        foreach (var item in combatState.IterateHookListeners())
        {
            if (item is IVitalityAmountModifier mod)
            {
                var num2 = mod.ModifyVitalityAdditive(creature, num, cardSource, cardPlay);
                num += num2;
                if (num2 != 0M)
                    abstractModelList.Add(item);
            }
        }

        foreach (var item in combatState.IterateHookListeners())
        {
            if (item is IVitalityAmountModifier mod)
            {
                var num2 = mod.ModifyVitalityMultiplicative(creature, num, cardSource, cardPlay);
                num *= num2;
                if (num2 != 0M)
                    abstractModelList.Add(item);
            }
        }

        modifiers = abstractModelList;
        return Math.Max(0m, num);
    }
    
    static async Task BeforeVitalityGained(
        CombatState combatState,
        Creature creature,
        Decimal amount,
        CardModel? cardSource)
    {
        foreach (var item in combatState.IterateHookListeners())
        {
            if (item is IVitalityHooks mod)
            {
                await mod.BeforeVitalityGained(creature, amount, cardSource);
                item.InvokeExecutionFinished();
            }
        }
    }
    
    static async Task AfterModifyingVitalityAmount(
        CombatState combatState,
        Decimal amount,
        CardModel? cardSource,
        CardPlay? cardPlay,
        IEnumerable<AbstractModel> modifiers)
    {
        foreach (var item in combatState.IterateHookListeners())
        {
            if (item is IVitalityHooks mod && modifiers.Contains(item))
            {
                await mod.AfterModifyingVitalityAmount(amount, cardSource, cardPlay);
                item.InvokeExecutionFinished();
            }
        }
    }
    
    static async Task AfterVitalityGained(
        CombatState combatState,
        Creature creature,
        Decimal amount,
        CardModel? cardSource)
    {
        foreach (var item in combatState.IterateHookListeners())
        {
            if (item is IVitalityHooks mod)
            {
                await mod.AfterVitalityGained(creature, amount, cardSource);
                item.InvokeExecutionFinished();
            }
        }
    }
    
    public class VitalityGainedEntry : CombatHistoryEntry
    {
        public int Amount { get; }

        public Creature Receiver => Actor;

        public CardPlay? CardPlay { get; }

        public override string Description
        {
            get => $"{GetId(Receiver)} gained {Amount} vitality";
        }

        public VitalityGainedEntry(
            int amount,
            CardPlay? cardPlay,
            Creature receiver,
            int roundNumber,
            CombatSide currentSide,
            CombatHistory history)
            : base(receiver, roundNumber, currentSide, history)
        {
            Amount = amount;
            CardPlay = cardPlay;
        }

        public static string GetId(Creature creature)
        {
            return !creature.IsPlayer ? creature.Monster.Id.Entry : creature.Player.Character.Id.Entry;
        }
    }
}