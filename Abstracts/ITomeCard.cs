using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Abstracts;

/// <summary>
/// Marks a card as a target for Darv's Dusty Tome relic.
/// If multiple cards are marked as tome cards for the same character, one will be chosen at random.
/// If this interface is not used, a random non-archaic tooth ancient card is used by Dusty Tome.
/// <br></br>Has a default method of checking character based on card pool, but can be explicitly set by implementing
/// TomeCharacter.
/// </summary>
public interface ITomeCard
{
    /// <summary>
    /// The character that this is the Dusty Tome card for.
    /// </summary>
    public CharacterModel TomeCharacter
    {
        get
        {
            if (this is CardModel card)
            {
                var character = ModelDb.AllCharacters.FirstOrDefault(c => c.CardPool.AllCardIds.Contains(card.Id));
                if (character != null)
                    return character;
            }

            throw new InvalidOperationException(
                "Default implementation of TomeCharacter in ITomeCard failed; override it manually.");
        }
    }
}