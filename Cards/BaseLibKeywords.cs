using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace BaseLib.Cards;

public class BaseLibKeywords
{
    /// See PurgePatch and BaseLibSingleton
    /// <summary>
    /// A card that removes itself from the deck when played.
    /// </summary>
    [CustomEnum] [KeywordProperties(AutoKeywordPosition.After)] public static CardKeyword Purge;
}