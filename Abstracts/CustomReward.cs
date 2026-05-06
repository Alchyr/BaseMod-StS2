using BaseLib.Extensions;
using Baselib.Patches.Content;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Abstracts;

/// <summary>
/// Delegate handler to indicate the expected structure of <c>CreateFromSerializable</c> methods
/// </summary>
public delegate T CreateRewardFromSave<out T>(SerializableReward save, Player player) where T : CustomReward;

/// <summary>
/// Class to inherit for creation a new type of reward.
/// "New type" does not mean this should be used for card pool rewards, or single card rewards. For those
/// use <see cref="CardReward"/> or <see cref="SpecialCardReward"/> respectively.
/// </summary>
public abstract class CustomReward(Player player) : Reward(player)
{
    /// <summary>
    /// Recommended for use in the reward's Description method. If additional variables are needed for the localization,
    /// add them using locString.Add("Key", value).
    /// </summary>
    /// <returns>A LocString for the gameplay_ui table with the key MODID-REWARD_CLASS_NAME</returns>
    public LocString GetLoc()
    {
        return new LocString("gameplay_ui", GetType().GetPrefix() + StringHelper.Slugify(GetType().Name));
    }
    
    /** Other Overrides
     * Populate - Prepares actual contents of rewards. Called if IsPopulated is false.
     * IsPopulated - Return true if reward is ready. Should be true after Populate is successfully called, or by default
     * if it's not necessary.
     * ToSerializable - If the reward has special information that must be saved.
     * Description
     * IconPath
     */
    
    /// <summary>
    /// Set the reward index after vanilla rewards by default
    /// </summary>
    public override int RewardsSetIndex => 9;

    /// <summary>
    /// Delegate to create your reward type from the saved data.
    /// The method reference must be of a <see langword="static"/> method
    /// </summary>
    /// <example>
    /// <code>
    /// // in MyCustomReward.cs
    /// public static MyCustomReward CreateFromSerializable(SerializableReward save, Player player)
    /// {
    ///     return new MyCustomReward(player) {
    ///         MyCustomNumber = save.GoldAmount
    ///     }
    /// }
    /// public override SerializableCustomReward&lt;CustomReward&gt; SerializeMethod => CreateFromSerializable;
    /// </code>
    /// </example>
    public abstract CreateRewardFromSave<CustomReward> DeserializeMethod { get; }

    /// <summary>
    /// Base method to handle registering your reward for serializing and deserializing in <see cref="RewardSynchronizer"/>
    /// Override this if you wish to manually register your reward with <see cref="CustomRewardPatches.RegisterCustomReward(RewardType, CreateRewardFromSave{T})"/>
    /// or by getting your own reference to the <see cref="RunLocationTargetedMessageBuffer"/> used for the <see cref="RewardSynchronizer"/> instance
    /// </summary>
    public virtual void Initialize()
    {
        if (DeserializeMethod != null)
        {
            if (DeserializeMethod.Target != null)
            {
                throw new ArgumentException($"DeserializeMethod of {GetType()} is not static");
            }
            BaseLibMain.Logger.Info($"Registering CustomReward deserializer for {GetType()}");
            CustomRewardPatches.RegisterCustomReward(RewardType, DeserializeMethod);
        }
        else
        {
            throw new NotImplementedException($"Custom Reward {GetType()} has not implemented a DeserializeMethod or overriden Initialize to register a deserializer for itself");
        }
    }
}

