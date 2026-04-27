using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Abstracts;

/// <summary>
/// An ease of use wrapper for CustomTemporaryPowerModel to simplify the process
/// </summary>
/// <typeparam name="TModel">The source of the power</typeparam>
/// <typeparam name="TPower">The power that will be applied to the target</typeparam>
public abstract class CustomTemporaryPowerModelWrapper<TModel, TPower> : CustomTemporaryPowerModel  where TModel : AbstractModel where TPower : PowerModel
{
    public override string CustomBigBetaIconPath => (Amount >= 0 == !InvertInternalPowerAmount ? "BaseLib/images/powers/big/baselib-power_temp_up.png" : "BaseLib/images/powers/big/baselib-power_temp_down.png");
    /// <summary>
    /// Placeholder small icon; you are recommended to override this.
    /// </summary>
    public override string CustomPackedIconPath => (Amount >= 0 == !InvertInternalPowerAmount ? "BaseLib/images/powers/baselib-power_temp_up.png" : "BaseLib/images/powers/baselib-power_temp_down.png");
    /// <summary>
    /// Placeholder large icon; you are recommended to override this.
    /// </summary>
    public override string CustomBigIconPath => (Amount >= 0 == !InvertInternalPowerAmount ? "BaseLib/images/powers/big/baselib-power_temp_up_big.png" : "BaseLib/images/powers/big/baselib-power_temp_down_big.png");

    public override AbstractModel OriginModel => ModelDb.GetById<AbstractModel>(ModelDb.GetId<TModel>());
    public override PowerModel InternallyAppliedPower => ModelDb.Power<TPower>();

    protected override Func<PlayerChoiceContext, Creature, decimal, Creature?, CardModel?, bool, Task> ApplyPowerFunc =>
        (context, target, amt, src, srcCard, silent) =>
        {
            return BetaMainCompatibility.PowerCmd_.Apply.
                InvokeGeneric<Task<TPower?>, TPower>(null, context, target, amt, src, srcCard, silent) ?? Task.CompletedTask;
        };
    
    
    public override LocString Title
    {
        get
        {
            switch (OriginModel)
            {
                case CardModel cardModel:
                    return cardModel.TitleLocString;
                case PotionModel potionModel:
                    return potionModel.Title;
                case RelicModel relicModel:
                    return relicModel.Title;
                case PowerModel powerModel:
                    return powerModel.Title;
                case OrbModel orbModel:
                    return orbModel.Title;
                case CharacterModel characterModel:
                    return characterModel.Title;
                case MonsterModel monsterModel:
                    return monsterModel.Title;
                case ActModel actModel:
                    return actModel.Title;
                default:
                    BaseLibMain.Logger.Warn($"Getting the 'Title' for the base model type of '{OriginModel.GetType().Name}' has not been implemented yet. Using default title.");
                    return new LocString("powers",  "BASELIB-CUSTOM_TEMPORARY_POWER_MODEL.title");
            }
        }
    }

    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            List<IHoverTip> items;
            switch (OriginModel)
            {
                case CardModel card:
                    items = [HoverTipFactory.FromCard(card)];
                    break;
                case PotionModel model:
                    items = [HoverTipFactory.FromPotion(model)];
                    break;
                case RelicModel relic:
                    items = HoverTipFactory.FromRelic(relic).ToList();
                    break;
                case PowerModel power:
                    items = [HoverTipFactory.FromPower(power)];
                    break;
                case ActModel:
                    items = [];
                    break;
                default:
                    BaseLibMain.Logger.Warn($"Getting the Hover Tips for the base model type of '{OriginModel.GetType().Name}' has not been implemented yet.");
                    items = [];
                    break;
            }
            items.Add(HoverTipFactory.FromPower(InternallyAppliedPower));
            return items;
        }
    }

    public override LocString Description => new LocString("powers", Amount > 0 == !InvertInternalPowerAmount ? "BASELIB-CUSTOM_TEMPORARY_POWER_MODEL.UP.description" : "BASELIB-CUSTOM_TEMPORARY_POWER_MODEL.DOWN.description");
    
    protected override string SmartDescriptionLocKey => Amount > 0 == !InvertInternalPowerAmount ? "BASELIB-CUSTOM_TEMPORARY_POWER_MODEL.UP.smartDescription" : "BASELIB-CUSTOM_TEMPORARY_POWER_MODEL.DOWN.smartDescription";

}