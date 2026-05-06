using System.Collections.Concurrent;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace BaseLib.Hooks;

/// <summary>
///     Per–card-type custom outline colors for the in-hand <see cref="MegaCrit.Sts2.Core.Nodes.Cards.NCardHighlight" />.
///     Applied after vanilla <see cref="NHandCardHolder.UpdateCard" /> via Harmony. Foreign providers (e.g. RitsuLib)
///     merge via <see cref="RegisterForeign" />.
/// </summary>
public static class ModCardHandOutlineRegistry
{
    private static readonly Func<CardModel, bool> ForeignPredicateAlreadySatisfied = static _ => true;

    private static int _sequence;
    private static int _foreignOrder;

    private static readonly ConcurrentDictionary<Type, List<RegisteredRule>> RulesByCardType = new();
    private static readonly Lock ForeignLock = new();
    private static readonly List<ForeignProvider> ForeignProviders = [];
    private static readonly List<ForeignDynamicProvider> ForeignDynamicProviders = [];

    /// <summary>
    ///     Registers a rule for <typeparamref name="TCard" />.
    /// </summary>
    public static void Register<TCard>(ModCardHandOutlineRule rule) where TCard : CardModel
    {
        Register(typeof(TCard), rule);
    }

    /// <summary>
    ///     Registers a rule for <paramref name="cardType" /> (<see cref="CardModel" /> subtype).
    /// </summary>
    public static void Register(Type cardType, ModCardHandOutlineRule rule)
    {
        ArgumentNullException.ThrowIfNull(cardType);
        ArgumentNullException.ThrowIfNull(rule.When);

        if (!typeof(CardModel).IsAssignableFrom(cardType))
            throw new ArgumentException(
                $"Type '{cardType.FullName}' must be a subtype of {typeof(CardModel).FullName}.",
                nameof(cardType));

        var seq = Interlocked.Increment(ref _sequence);
        var wrapped = new RegisteredRule(rule, seq);

        RulesByCardType.AddOrUpdate(
            cardType,
            _ => [wrapped],
            (_, existing) =>
            {
                var copy = new List<RegisteredRule>(existing) { wrapped };
                return copy;
            });
    }

    /// <summary>
    ///     Merges outline evaluation from another assembly (e.g. RitsuLib). The delegate must return
    ///     <see langword="null" /> when no rule applies, otherwise paint fields only — the foreign registry has already
    ///     evaluated <c>When</c>. Uses <see cref="ValueTuple" /> so the boundary stays a nullable struct (no heap boxing).
    /// </summary>
    public static void RegisterForeign(string modId, string sourceId,
        Func<CardModel, (Color Color, int Priority, bool VisibleWhenUnplayable)?> evaluateBestFromForeign)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(evaluateBestFromForeign);

        var order = Interlocked.Increment(ref _foreignOrder);
        lock (ForeignLock)
        {
            ForeignProviders.Add(new ForeignProvider(evaluateBestFromForeign, order));
        }
    }

    /// <summary>
    ///     Merges dynamic outline evaluation from another assembly. The delegate returns current paint resolver and metadata.
    /// </summary>
    public static void RegisterForeignDynamic(string modId, string sourceId,
        Func<CardModel, (Func<Color> ResolveColor, int Priority, bool VisibleWhenUnplayable)?> evaluateBestFromForeign)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(evaluateBestFromForeign);

        var order = Interlocked.Increment(ref _foreignOrder);
        lock (ForeignLock)
        {
            ForeignDynamicProviders.Add(new ForeignDynamicProvider(evaluateBestFromForeign, order));
        }
    }

    /// <summary>
    ///     Clears all rules and foreign providers (tests / tooling).
    /// </summary>
    public static void ClearForTests()
    {
        RulesByCardType.Clear();
        lock (ForeignLock)
        {
            ForeignProviders.Clear();
            ForeignDynamicProviders.Clear();
        }
    }

    /// <summary>
    ///     Applies the best matching registered outline for this holder.
    /// </summary>
    /// <returns><see langword="true" /> if a rule was applied.</returns>
    public static bool TryRefreshOutlineForHolder(NHandCardHolder? holder)
    {
        if (holder == null || !holder.IsNodeReady() || holder.CardNode?.Model is not { } model)
            return false;

        var rule = EvaluateBest(model);
        if (!rule.HasValue)
            return false;

        ModCardHandOutlinePatchHelper.ApplyHighlight(holder, model, rule.Value);
        return true;
    }

    /// <summary>
    ///     Applies outline only when the winning rule uses <see cref="ModCardHandOutlineRule.DynamicColor" />.
    /// </summary>
    public static bool TryRefreshDynamicOutlineForHolder(NHandCardHolder? holder)
    {
        if (holder == null || !holder.IsNodeReady() || holder.CardNode?.Model is not { } model)
            return false;

        var rule = EvaluateBest(model);
        if (!rule.HasValue || rule.Value.DynamicColor == null)
            return false;

        ModCardHandOutlinePatchHelper.ApplyHighlight(holder, model, rule.Value);
        return true;
    }

    internal static ModCardHandOutlineRule? EvaluateBest(CardModel model)
    {
        var local = EvaluateLocalBest(model);
        ForeignCandidate? foreignBest = null;

        List<ForeignProvider> snapshot;
        lock (ForeignLock)
        {
            snapshot = [..ForeignProviders];
        }

        foreach (var provider in snapshot)
        {
            (Color Color, int Priority, bool VisibleWhenUnplayable)? foreignPaint;
            try
            {
                foreignPaint = provider.Evaluate(model);
            }
            catch
            {
                continue;
            }

            if (foreignPaint is not { } paint)
                continue;

            var candidate = new ModCardHandOutlineRule(ForeignPredicateAlreadySatisfied, paint.Color, paint.Priority,
                paint.VisibleWhenUnplayable);

            if (foreignBest is null ||
                RuleWins(candidate, provider.Order, foreignBest.Value.Rule, foreignBest.Value.Order))
                foreignBest = new ForeignCandidate(candidate, provider.Order);
        }

        List<ForeignDynamicProvider> dynamicSnapshot;
        lock (ForeignLock)
        {
            dynamicSnapshot = [..ForeignDynamicProviders];
        }

        foreach (var provider in dynamicSnapshot)
        {
            (Func<Color> ResolveColor, int Priority, bool VisibleWhenUnplayable)? foreignPaint;
            try
            {
                foreignPaint = provider.Evaluate(model);
            }
            catch
            {
                continue;
            }

            if (foreignPaint is not { } paint || paint.ResolveColor == null)
                continue;

            var candidate = new ModCardHandOutlineRule(
                ForeignPredicateAlreadySatisfied,
                paint.ResolveColor(),
                paint.Priority,
                paint.VisibleWhenUnplayable)
            {
                DynamicColor = _ => paint.ResolveColor(),
            };

            if (foreignBest is null ||
                RuleWins(candidate, provider.Order, foreignBest.Value.Rule, foreignBest.Value.Order))
                foreignBest = new ForeignCandidate(candidate, provider.Order);
        }

        switch (local)
        {
            case null when foreignBest is null:
                return null;
            case null:
                return foreignBest.Value.Rule;
        }

        if (foreignBest is null)
            return local.Value.Rule;

        return RuleWins(foreignBest.Value.Rule, foreignBest.Value.Order, local.Value.Rule, local.Value.Sequence)
            ? foreignBest.Value.Rule
            : local.Value.Rule;
    }

    private static RegisteredRule? EvaluateLocalBest(CardModel model)
    {
        RegisteredRule? best = null;

        for (var t = model.GetType();
             t != null && typeof(CardModel).IsAssignableFrom(t);
             t = t.BaseType)
        {
            if (!RulesByCardType.TryGetValue(t, out var list))
                continue;

            foreach (var entry in list.Where(entry => entry.Rule.When(model)).Where(entry => best is null
                         || entry.Rule.Priority > best.Value.Rule.Priority
                         || (entry.Rule.Priority == best.Value.Rule.Priority &&
                             entry.Sequence > best.Value.Sequence)))
                best = entry;
        }

        return best;
    }

    private static bool RuleWins(ModCardHandOutlineRule challenger, int challengerOrder,
        ModCardHandOutlineRule incumbent,
        int incumbentOrder)
    {
        if (challenger.Priority != incumbent.Priority)
            return challenger.Priority > incumbent.Priority;

        return challengerOrder > incumbentOrder;
    }

    private readonly record struct RegisteredRule(ModCardHandOutlineRule Rule, int Sequence);

    private readonly record struct ForeignProvider(
        Func<CardModel, (Color Color, int Priority, bool VisibleWhenUnplayable)?> Evaluate,
        int Order);

    private readonly record struct ForeignDynamicProvider(
        Func<CardModel, (Func<Color> ResolveColor, int Priority, bool VisibleWhenUnplayable)?> Evaluate,
        int Order);

    private readonly record struct ForeignCandidate(ModCardHandOutlineRule Rule, int Order);
}