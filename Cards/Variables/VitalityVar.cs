using BaseLib.Extensions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace BaseLib.Cards.Variables;

public class VitalityVar : DynamicVar
{
    public const string Key = "Vitality";

    public VitalityVar(decimal baseValue) : base(Key, baseValue)
    {
        this.WithTooltip();
    }
}