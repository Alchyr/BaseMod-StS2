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
    private readonly List<CardKeyword> _cardKeywords = [];
    private readonly List<DynamicVar> _dynamicVars = [];
    private readonly List<TooltipSource> _hoverTips = [];
    private readonly HashSet<CardTag> _tags = [];
    private bool _hasCalculatedVar = false;

    protected sealed override IEnumerable<DynamicVar> CanonicalVars => _dynamicVars;
    public sealed override IEnumerable<CardKeyword> CanonicalKeywords => _cardKeywords;
    protected sealed override IEnumerable<IHoverTip> ExtraHoverTips => _hoverTips.Select(tip => tip.Tip(this));
    protected sealed override HashSet<CardTag> CanonicalTags => _tags;

    protected ConstructedCardModel WithVars(params DynamicVar[] vars)
    {
        foreach (var dynVar in vars)
        {
            _dynamicVars.Add(dynVar);
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
    protected ConstructedCardModel WithVar(string name, int baseVal)
    {
        _dynamicVars.Add(new DynamicVar(name, baseVal));
        return this;
    }
    
    /// <summary>
    /// Generates a <seealso cref="BlockVar"/>BlockVar with given base value.
    /// </summary>
    /// <param name="baseVal"></param>
    /// <returns></returns>
    protected ConstructedCardModel WithBlock(int baseVal)
    {
        _dynamicVars.Add(new BlockVar(baseVal, ValueProp.Move));
        return this;
    }
    
    /// <summary>
    /// Generates a <seealso cref="DamageVar"/>DamageVar with given base value.
    /// </summary>
    /// <param name="baseVal"></param>
    /// <returns></returns>
    protected ConstructedCardModel WithDamage(int baseVal)
    {
        _dynamicVars.Add(new DamageVar(baseVal, ValueProp.Move));
        return this;
    }

    /// <summary>
    /// Generates a <seealso cref="CardsVar"/>CardsVar with given base value.
    /// </summary>
    /// <param name="baseVal"></param>
    /// <returns></returns>
    protected ConstructedCardModel WithCards(int baseVal)
    {
        _dynamicVars.Add(new CardsVar(baseVal));
        return this;
    }
    
    /// <summary>
    /// Generates a <seealso cref="PowerVar{T}"/>PowerVar and adds a tooltip. You can also just pass a PowerVar to <seealso cref="WithVars"/>WithVars.
    /// </summary>
    /// <param name="baseVal"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    protected ConstructedCardModel WithPower<T>(int baseVal) where T : PowerModel
    {
        _dynamicVars.Add(new PowerVar<T>(baseVal));
        _hoverTips.Add(new(_=>HoverTipFactory.FromPower<T>()));
        return this;
    }

    /// <summary>
    /// Generates a <seealso cref="PowerVar{T}"/>PowerVar with the specified name and adds a tooltip. You can also just pass a PowerVar to <seealso cref="WithVars"/>WithVars.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="baseVal"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    protected ConstructedCardModel WithPower<T>(string name, int baseVal) where T : PowerModel
    {
        _dynamicVars.Add(new PowerVar<T>(name, baseVal));
        _hoverTips.Add(new(_=>HoverTipFactory.FromPower<T>()));
        return this;
    }
    
    protected ConstructedCardModel WithTags(params CardTag[] tags)
    {
        foreach (var cardTag in tags) _tags.Add(cardTag);
        return this;
    }

    //TODO - setup arbitrary number of calculated variables
    /// <summary>
    /// Variable value is baseVal + bonus
    /// </summary>
    /// <param name="name"></param>
    /// <param name="baseVal"></param>
    /// <param name="bonus"></param>
    protected ConstructedCardModel WithCalculatedVar(string name, int baseVal, Func<CardModel, Creature?, decimal> bonus)
    {
        SetupCalculatedVar(new CalculatedVar(name), baseVal, 1, bonus);
        return this;
    }

    /// <summary>
    /// Variable value is baseVal + (multVal * mult)
    /// </summary>
    /// <param name="name"></param>
    /// <param name="baseVal"></param>
    /// <param name="multVal"></param>
    /// <param name="mult"></param>
    protected ConstructedCardModel WithCalculatedVar(string name, int baseVal, int multVal,
        Func<CardModel, Creature?, decimal> mult)
    {
        SetupCalculatedVar(new CalculatedVar(name), baseVal, multVal, mult);
        return this;
    }
    
    /// <summary>
    /// Resulting variable name is "CalculatedBlock"
    /// Variable value is baseVal + bonus
    /// </summary>
    /// <param name="baseVal"></param>
    /// <param name="bonus"></param>
    /// <param name="props"></param>
    protected ConstructedCardModel WithCalculatedBlock(int baseVal, Func<CardModel, Creature?, decimal> bonus, ValueProp props = ValueProp.Move)
    {
        SetupCalculatedVar(new CalculatedBlockVar(props), baseVal, 1, bonus);
        return this;
    }
    /// <summary>
    /// Resulting variable name is "CalculatedBlock"
    /// Variable value is baseVal + (multVal * mult)
    /// </summary>
    /// <param name="baseVal"></param>
    /// <param name="multVal"></param>
    /// <param name="mult"></param>
    /// <param name="props"></param>
    protected ConstructedCardModel WithCalculatedBlock(int baseVal, int multVal,
        Func<CardModel, Creature?, decimal> mult, ValueProp props = ValueProp.Move)
    {
        SetupCalculatedVar(new CalculatedBlockVar(props), baseVal, multVal, mult);
        return this;
    }
    
    /// <summary>
    /// Resulting variable name is "CalculatedDamage"
    /// Variable value is baseVal + bonus
    /// </summary>
    /// <param name="baseVal"></param>
    /// <param name="bonus"></param>
    /// <param name="props"></param>
    protected ConstructedCardModel WithCalculatedDamage(int baseVal, Func<CardModel, Creature?, decimal> bonus, ValueProp props = ValueProp.Move)
    {
        SetupCalculatedVar(new CalculatedDamageVar(props), baseVal, 1, bonus);
        return this;
    }
    /// <summary>
    /// Resulting variable name is "CalculatedDamage"
    /// Variable value is baseVal + (multVal * mult)
    /// </summary>
    /// <param name="baseVal"></param>
    /// <param name="multVal"></param>
    /// <param name="mult"></param>
    /// <param name="props"></param>
    protected ConstructedCardModel WithCalculatedDamage(int baseVal, int multVal,
        Func<CardModel, Creature?, decimal> mult, ValueProp props = ValueProp.Move)
    {
        SetupCalculatedVar(new CalculatedDamageVar(props), baseVal, multVal, mult);
        return this;
    }

    private void SetupCalculatedVar(CalculatedVar var, int baseVal, int multVal,
        Func<CardModel, Creature?, decimal> mult)
    {
        if (_hasCalculatedVar) throw new Exception("Cards only support one calculated variable currently");
        _hasCalculatedVar = true;

        _dynamicVars.Add(new CalculationBaseVar(baseVal));
        _dynamicVars.Add(new CalculationExtraVar(multVal));
        _dynamicVars.Add(var.WithMultiplier(mult));
    }

    protected ConstructedCardModel WithKeywords(params CardKeyword[] keywords)
    {
        _cardKeywords.AddRange(keywords);
        return this;
    }

    /// <summary>
    /// Can accept PowerModel, CardKeyword, CardModel, PotionModel
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
}