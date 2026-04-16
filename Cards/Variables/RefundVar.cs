using BaseLib.Extensions;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Cards.Variables;

public class RefundVar : DynamicVar
{
    public const string Key = "Refund";

    public RefundVar(decimal refundAmount) : base(Key, refundAmount)
    {
        this.WithTooltip();
    }

    /*public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        PreviewValue = IntValue;
    }*/
}