using BaseLib.Patches.Localization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Abstracts;

/// <summary>
/// A generic version of the base games Temporary Strength and Dexterity Power with small functionality improvements
/// </summary>
public abstract class CustomTemporaryPowerModel : CustomPowerModel, ITemporaryPower, IAddDumbVariablesToPowerDescription
{
     private const string LocTurnEndBoolVar = "UntilEndOfOtherSideTurn";
     
     public void AddDumbVariablesToPowerDescription(LocString description)
     {
         description.Add("TemporaryPowerTitle", this.InternallyAppliedPower.Title);
     }

    protected abstract Func<PlayerChoiceContext, Creature, decimal, Creature?, CardModel?, bool, Task> ApplyPowerFunc { get; }
    public abstract PowerModel InternallyAppliedPower { get; }
    public abstract AbstractModel OriginModel { get; }
    protected virtual bool UntilEndOfOtherSideTurn => false;
    protected virtual int LastForXExtraTurns => 0;
    
    public override PowerType Type => InternallyAppliedPower.Type;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool AllowNegative => true;
    public override bool IsInstanced => LastForXExtraTurns != 0;
    protected virtual bool InvertInternalPowerAmount => false;
    
    // The whole IgnoreNextInstance thing ONLY exists because of the Misery card
    // Check Misery.DoHackyThingsForSpecificPowers() for usage
    private bool _shouldIgnoreNextInstance;
    public void IgnoreNextInstance() => _shouldIgnoreNextInstance = true;
    
    // Only used for localization purposes
    protected override IEnumerable<DynamicVar> CanonicalVars => [new RepeatVar(0), new BoolVar(LocTurnEndBoolVar, false)];

    public override async Task BeforeApplied(Creature target, decimal amount, Creature? applier, CardModel? cardSource)
    {
        var powerSource = this;
        if (InternallyAppliedPower is CustomTemporaryPowerModel)
        {
            // This could lead to infinite recursion if someone makes a mistake and publishes it. So just say no to any attempt.
            BaseLibMain.Logger.Warn($"Don't put TemporaryPowerModels into a TemporaryPowerModel. Attempted to apply power '{InternallyAppliedPower.GetType().Name}' in power '{this.GetType().Name}'. Power will not be applied!");
            return;
        }
        if (_shouldIgnoreNextInstance)
        {
            _shouldIgnoreNextInstance = false;
        }
        else
        {
            DynamicVars.Repeat.BaseValue = LastForXExtraTurns;
            DynamicVars[LocTurnEndBoolVar].BaseValue = Convert.ToDecimal(UntilEndOfOtherSideTurn);
            await ApplyPowerFunc(new ThrowingPlayerChoiceContext(), target, InvertInternalPowerAmount ? -amount : amount, applier, cardSource, true);
        }
    }

    
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext context, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        var powerSource = this;
        if (InternallyAppliedPower is CustomTemporaryPowerModel)
            return;
        if (amount == powerSource.Amount || power != powerSource)
            return;
        if (powerSource._shouldIgnoreNextInstance)
            powerSource._shouldIgnoreNextInstance = false;
        else
            await ApplyPowerFunc(context, powerSource.Owner, InvertInternalPowerAmount ? -amount : amount, applier, cardSource, true);
    }


    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        var powerSource = this;
        if (InternallyAppliedPower is CustomTemporaryPowerModel)
        {
            await PowerCmd.Remove(powerSource);
            return;
        }
        if ((!UntilEndOfOtherSideTurn && side != powerSource.Owner.Side) || (UntilEndOfOtherSideTurn && side == powerSource.Owner.Side))
            return;
        if (powerSource.DynamicVars.Repeat.BaseValue > 0)
        {
            powerSource.DynamicVars.Repeat.UpgradeValueBy(-1);
            return;
        }

        powerSource.Flash();
        await ApplyPowerFunc(choiceContext, powerSource.Owner, InvertInternalPowerAmount ? powerSource.Amount : -powerSource.Amount, powerSource.Owner, null, true);
        await PowerCmd.Remove(powerSource);
    }

}