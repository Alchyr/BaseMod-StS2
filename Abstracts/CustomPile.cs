using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace BaseLib.Abstracts;

/// <summary>
/// Patches for functionality found in BaseLib.Patches.Content.CustomPiles<seealso cref="BaseLib.Patches.Content.CustomPiles"/>
/// </summary>
public abstract class CustomPile : CardPile
{
    /// <summary>
    /// Define a PileType CustomEnum inside your CustomPile class and use it as the parameter for this constructor.
    /// </summary>
    /// <param name="pileType"></param>
    public CustomPile(PileType pileType) : base(pileType)
    {
    }

    /// <summary>
    /// For a CardPile like the hand, where cards are directly shown.
    /// This does not handle keeping the NCards visible; it just says that they should be.
    /// Changes how cards are moved between piles.
    /// </summary>
    public abstract bool CardShouldBeVisible(CardModel card);
    /// <summary>
    /// For something like the exhaust pile where cards have an exhaust effect applied rather than using the normal card pile transition.
    /// Will be ignored if CardsVisible is true due to the necessity of allowing an NCard to be created.
    /// </summary>
    public virtual bool NeedsCustomTransitionVisual { get => false; }

    public abstract Vector2 GetTargetPosition(CardModel model, Vector2 size);

    public virtual NCard? GetNCard(CardModel card)
    {
        return null;
    }

    /// <summary>
    /// Create a custom tween when a card is moved to this pile. Return true if a custom tween is created.
    /// Otherwise default tween used to move to discard/draw with GetTargetPosition will be used.
    /// </summary>
    /// <param name="tween"></param>
    /// <param name="card"></param>
    /// <param name="cardNode"></param>
    /// <param name="oldPile"></param>
    /// <returns></returns>
    public virtual bool CustomTween(Tween tween, CardModel card, NCard cardNode, CardPile oldPile)
    {
        return false;
    }
}
