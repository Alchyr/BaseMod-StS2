using Godot;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Hooks;

/// <summary>
///     Custom hand-card outline tint for <see cref="MegaCrit.Sts2.Core.Nodes.Cards.NCardHighlight" /> after vanilla
///     playable / gold / red. Register with <see cref="ModCardHandOutlineRegistry" />.
/// </summary>
/// <param name="When">When this returns true for the card instance, the outline color may apply.</param>
/// <param name="Color">Godot modulate color (alpha is respected; vanilla highlights use ~0.98).</param>
/// <param name="Priority">
///     When several rules match, the highest <paramref name="Priority" /> wins; ties favor the most recently registered
///     rule.
/// </param>
/// <param name="VisibleWhenUnplayable">
///     If true, the highlight is forced visible with this color even when the card is not playable and vanilla would not
///     show gold/red (still only while combat is in progress).
/// </param>
public readonly record struct ModCardHandOutlineRule(
    Func<CardModel, bool> When,
    Color Color,
    int Priority = 0,
    bool VisibleWhenUnplayable = false)
{
    /// <summary>
    ///     Optional dynamic color resolver. When assigned and <see cref="When" /> passes, this is evaluated on refresh
    ///     to produce current outline color.
    /// </summary>
    public Func<CardModel, Color>? DynamicColor { get; init; }

    /// <summary>
    ///     Creates a rule with a dynamic color resolver.
    /// </summary>
    public static ModCardHandOutlineRule Dynamic(
        Func<CardModel, bool> when,
        Func<CardModel, Color> colorWhen,
        int priority = 0,
        bool visibleWhenUnplayable = false)
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(colorWhen);
        return new ModCardHandOutlineRule(when, Colors.White, priority, visibleWhenUnplayable)
        {
            DynamicColor = colorWhen,
        };
    }

    internal Color ResolveColor(CardModel card)
    {
        return DynamicColor?.Invoke(card) ?? Color;
    }
}