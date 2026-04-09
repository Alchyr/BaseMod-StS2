using BaseLib.Extensions;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Abstracts;

public abstract class ConstructedCardModel(
    int baseCost,
    CardType type,
    CardRarity rarity,
    TargetType target,
    bool showInCardLibrary = true,
    bool autoAdd = true)
    : CustomCardModel(baseCost, type, rarity, target, showInCardLibrary, autoAdd)
{
    protected enum UpgradeType
    {
        None,
        Add,
        Remove
    }
    
    private readonly List<CardKeyword> _cardKeywords = [];
    /// <summary>
    /// Keywords that will be modified on upgrade.
    /// </summary>
    protected readonly List<(CardKeyword, UpgradeType)> UpgradeKeywords = [];
    private readonly List<DynamicVar> _constructedDynamicVars = [];
    private readonly List<TooltipSource> _hoverTips = [];
    private readonly HashSet<CardTag> _constructedTags = [];
    private bool _hasCalculatedVar = false;

    protected sealed override IEnumerable<DynamicVar> CanonicalVars => _constructedDynamicVars;
    public sealed override IEnumerable<CardKeyword> CanonicalKeywords => _cardKeywords;
    protected sealed override IEnumerable<IHoverTip> ExtraHoverTips => _hoverTips.Select(tip => tip.Tip(this));
    protected sealed override HashSet<CardTag> CanonicalTags => _constructedTags;

    protected ConstructedCardModel WithVars(params DynamicVar[] vars)
    {
        foreach (var dynVar in vars)
        {
            _constructedDynamicVars.Add(dynVar);
            var type = dynVar.GetType();
            if (!type.IsGenericType) continue;
            
            foreach (var arg in type.GetGenericArguments())
            {
                if (!arg.IsAssignableTo(typeof(PowerModel))) continue;
                WithTip(arg);
            }
        }
        return this;
    }
    protected ConstructedCardModel WithVar(string name, int baseVal, int upgrade = 0)
    {
        _constructedDynamicVars.Add(new DynamicVar(name, baseVal).WithUpgrade(upgrade));
        return this;
    }
    
    /// <summary>
    /// Generates a <seealso cref="BlockVar"/>BlockVar with given base value.
    /// </summary>
    protected ConstructedCardModel WithBlock(int baseVal, int upgrade = 0)
    {
        _constructedDynamicVars.Add(new BlockVar(baseVal, ValueProp.Move).WithUpgrade(upgrade));
        return this;
    }
    
    /// <summary>
    /// Generates a <seealso cref="DamageVar"/>DamageVar with given base value.
    /// </summary>
    protected ConstructedCardModel WithDamage(int baseVal, int upgrade = 0)
    {
        _constructedDynamicVars.Add(new DamageVar(baseVal, ValueProp.Move).WithUpgrade(upgrade));
        return this;
    }

    /// <summary>
    /// Generates a <seealso cref="CardsVar"/>CardsVar with given base value.
    /// </summary>
    protected ConstructedCardModel WithCards(int baseVal, int upgrade = 0)
    {
        var dynVar = new CardsVar(baseVal).WithUpgrade(upgrade);
        _constructedDynamicVars.Add(dynVar);
        return this;
    }
    
    /// <summary>
    /// Generates a <seealso cref="PowerVar{T}"/>PowerVar and adds a tooltip. You can also just pass a PowerVar to <seealso cref="WithVars"/>WithVars.
    /// </summary>
    protected ConstructedCardModel WithPower<T>(int baseVal, int upgrade = 0) where T : PowerModel
    {
        _constructedDynamicVars.Add(new PowerVar<T>(baseVal).WithUpgrade(upgrade));
        _hoverTips.Add(new(_=>HoverTipFactory.FromPower<T>()));
        return this;
    }

    /// <summary>
    /// Generates a <seealso cref="PowerVar{T}"/>PowerVar with the specified name and adds a tooltip. You can also just pass a PowerVar to <seealso cref="WithVars"/>WithVars.
    /// </summary>
    protected ConstructedCardModel WithPower<T>(string name, int baseVal, int upgrade = 0) where T : PowerModel
    {
        _constructedDynamicVars.Add(new PowerVar<T>(name, baseVal).WithUpgrade(upgrade));
        _hoverTips.Add(new(_=>HoverTipFactory.FromPower<T>()));
        return this;
    }
    
    protected ConstructedCardModel WithTags(params CardTag[] tags)
    {
        foreach (var cardTag in tags) _constructedTags.Add(cardTag);
        return this;
    }

    //TODO - setup arbitrary number of calculated variables
    //set upgrade for bonus also?
    /// <summary>
    /// Variable value is baseVal + bonus
    /// </summary>
    protected ConstructedCardModel WithCalculatedVar(string name, int baseVal, 
        Func<CardModel, Creature?, decimal> bonus, int upgrade = 0, int bonusUpgrade = 0)
    {
        SetupCalculatedVar(new CalculatedVar(name), baseVal, 1, bonus, upgrade, bonusUpgrade);
        return this;
    }

    /// <summary>
    /// Variable value is baseVal + (multVal * mult)
    /// </summary>
    protected ConstructedCardModel WithCalculatedVar(string name, int baseVal, int multVal,
        Func<CardModel, Creature?, decimal> mult, int upgrade = 0, int bonusUpgrade = 0)
    {
        SetupCalculatedVar(new CalculatedVar(name), baseVal, multVal, mult, upgrade, bonusUpgrade);
        return this;
    }
    
    /// <summary>
    /// Resulting variable name is "CalculatedBlock"
    /// Variable value is baseVal + bonus
    /// </summary>
    protected ConstructedCardModel WithCalculatedBlock(int baseVal, Func<CardModel, Creature?, decimal> bonus, 
        ValueProp props = ValueProp.Move, int upgrade = 0, int bonusUpgrade = 0)
    {
        SetupCalculatedVar(new CalculatedBlockVar(props), baseVal, 1, bonus, upgrade, bonusUpgrade);
        return this;
    }
    /// <summary>
    /// Resulting variable name is "CalculatedBlock"
    /// Variable value is baseVal + (multVal * mult)
    /// </summary>
    protected ConstructedCardModel WithCalculatedBlock(int baseVal, int multVal,
        Func<CardModel, Creature?, decimal> mult, ValueProp props = ValueProp.Move, int upgrade = 0, int bonusUpgrade = 0)
    {
        SetupCalculatedVar(new CalculatedBlockVar(props), baseVal, multVal, mult, upgrade, bonusUpgrade);
        return this;
    }
    
    /// <summary>
    /// Resulting variable name is "CalculatedDamage"
    /// Variable value is baseVal + bonus
    /// </summary>
    protected ConstructedCardModel WithCalculatedDamage(int baseVal, Func<CardModel, Creature?, decimal> bonus, 
        ValueProp props = ValueProp.Move, int upgrade = 0, int bonusUpgrade = 0)
    {
        SetupCalculatedVar(new CalculatedDamageVar(props), baseVal, 1, bonus, upgrade, bonusUpgrade);
        return this;
    }
    /// <summary>
    /// Resulting variable name is "CalculatedDamage"
    /// Variable value is baseVal + (multVal * mult)
    /// </summary>
    protected ConstructedCardModel WithCalculatedDamage(int baseVal, int multVal,
        Func<CardModel, Creature?, decimal> mult, ValueProp props = ValueProp.Move, int upgrade = 0, int bonusUpgrade = 0)
    {
        SetupCalculatedVar(new CalculatedDamageVar(props), baseVal, multVal, mult, upgrade, bonusUpgrade);
        return this;
    }

    private void SetupCalculatedVar(CalculatedVar var, int baseVal, int multVal,
        Func<CardModel, Creature?, decimal> mult, int upgrade, int bonusUpgrade)
    {
        if (_hasCalculatedVar) throw new Exception("Cards only support one calculated variable currently");
        _hasCalculatedVar = true;

        _constructedDynamicVars.Add(new CalculationBaseVar(baseVal).WithUpgrade(upgrade));
        _constructedDynamicVars.Add(var is CalculatedDamageVar
            ? new ExtraDamageVar(multVal).WithUpgrade(bonusUpgrade)
            : new CalculationExtraVar(multVal).WithUpgrade(bonusUpgrade));
        
        _constructedDynamicVars.Add(var.WithMultiplier(mult));
    }

    /// <summary>
    /// Adds multiple keywords to the card.
    /// </summary>
    protected ConstructedCardModel WithKeywords(params CardKeyword[] keywords)
    {
        _cardKeywords.AddRange(keywords);
        return this;
    }

    /// <summary>
    /// Adds a keyword to the card. If <paramref name="removeOnUpgrade"/> is true, the keyword will be removed when the card is upgraded.
    /// </summary>
    protected ConstructedCardModel WithKeyword(CardKeyword keyword, UpgradeType upgradeType = UpgradeType.None)
    {
        if (upgradeType != UpgradeType.Add) _cardKeywords.Add(keyword);
        if (upgradeType != UpgradeType.None) UpgradeKeywords.Add((keyword, upgradeType));
        return this;
    }

    internal int? CostUpgrade;

    /// <summary>
    /// Adjusts the card's energy cost when upgraded. Use negative values to reduce cost, positive to increase.
    /// </summary>
    protected ConstructedCardModel WithCostUpgradeBy(int amount)
    {
        CostUpgrade = amount;
        return this;
    }

    /// <summary>
    /// Can accept PowerModel, CardKeyword, CardModel, PotionModel, StaticHoverTip, EnchantmentModel
    /// </summary>
    /// <param name="tipSource"></param>
    /// <returns></returns>
    protected ConstructedCardModel WithTip(TooltipSource tipSource)
    {
        _hoverTips.Add(tipSource);
        return this;
    }
    
    protected ConstructedCardModel WithEnergyTip()
    {
        _hoverTips.Add(new(HoverTipFactory.ForEnergy));
        return this;
    }
    
    /// <summary>
    /// Called after the card's normal upgrade to handle upgrades declared in the ConstructedCardModel.
    /// </summary>
    public void ConstructedUpgrade()
    {
        foreach (var keyword in UpgradeKeywords)
        {
            switch (keyword.Item2)
            {
                case UpgradeType.Add:
                    AddKeyword(keyword.Item1);
                    break;
                case UpgradeType.Remove:
                    RemoveKeyword(keyword.Item1);
                    break;
            }
        }
        if (CostUpgrade.HasValue)
            EnergyCost.UpgradeBy(CostUpgrade.Value);
    }
}