using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Localization;

namespace BaseLib.Extensions;

/// <summary>
/// Extension class to match the pattern of vanilla <see cref="CardSelectorPrefs"/> <see cref="LocString"/> fields
/// </summary>
public static class CardSelectorPrefsExtensions
{
    /// <summary>
    /// Static reference to "Transform and Upgrade" selection screen localization
    /// </summary>
    public static LocString TransformAndUpgradeSelectionPrompt =>
        new LocString(CardSelectorPrefs._cardSelectionLocFilePath, "TO_TRANSFORM_AND_UPGRADE");
}
