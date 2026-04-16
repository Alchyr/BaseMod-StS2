using MegaCrit.Sts2.Core.Entities.Players;

namespace BaseLib.Hooks;

/// <summary>
/// Interface for modifying the player's maximum hand size.
/// Implement <c>ModifyMaxHandSize</c> to modify the max hand size, and <c>ModifyMaxHandSizeLate</c> to modify it after <c>ModifyMaxHandSize</c> have been applied.
/// </summary>
public interface IMaxHandSizeModifier
{

    /// <summary>
    /// Modify the player's maximum hand size. This is called before <c>ModifyMaxHandSizeLate</c>.
    /// </summary>
    /// <param name="player">The player whose max hand size to modify.</param>
    /// <param name="currentMaxHandSize">The current maximum hand size that has been modified through iteration.</param>
    /// <returns>The modified maximum hand size.</returns>
    int ModifyMaxHandSize(Player player, int currentMaxHandSize) => currentMaxHandSize;

    /// <summary>
    /// Modify the player's maximum hand size. This is called after <c>ModifyMaxHandSize</c>.
    /// </summary>
    /// <param name="player">The player whose max hand size to modify.</param>
    /// <param name="currentMaxHandSize">The current maximum hand size that has been modified through iteration.</param>
    /// <returns>The modified maximum hand size.</returns>
    int ModifyMaxHandSizeLate(Player player, int currentMaxHandSize) => currentMaxHandSize;
}