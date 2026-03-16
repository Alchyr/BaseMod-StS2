using MegaCrit.Sts2.Core.Models.Powers;

namespace BaseLib.Abstracts;

public abstract class CustomTemporaryDexterityPower : TemporaryDexterityPower, ICustomPowerModel
{
    public virtual string? CustomPackedIconPath => null;
    public virtual string? CustomBigIconPath => null;
    public virtual string? CustomBigBetaIconPath => null;
}
