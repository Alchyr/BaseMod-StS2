using Baselib.Patches.Content;
using BaseLib.Common.Rewards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Abstracts;

/// <summary>
/// Delegate handler to indicate the expected structure of <c>CreateFromSerializable</c> methods
/// </summary>
public delegate T SerializableCustomReward<out T>(SerializableReward save, Player player) where T : CustomReward;

/// <summary>
/// Class to inherit for creation a new type of reward.\n
/// "New type" does not mean this should be used for card pool rewards, or single card rewards.
/// Use <see cref="CardReward"/> and <see cref="SpecialCardReward"/> respectively,
/// though be mindful that you don't use a constructor that is unsupported by the base-game structure.
/// <seealso cref="CardTransformReward"/>
/// </summary>
public abstract class CustomReward(Player player) : Reward(player)
{
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
    public abstract SerializableCustomReward<CustomReward> SerializeMethod { get; }

    /// <summary>
    /// Base method to handle registering your reward for serializing and deserializing in <see cref="RewardSynchronizer"/>
    /// Override this if you wish to manually register your reward with <see cref="CustomRewardPatches.RegisterCustomReward(RewardType, SerializableCustomReward{CustomReward})"/>
    /// or by getting your own reference to the <see cref="RunLocationTargetedMessageBuffer"/> used for the <see cref="RewardSynchronizer"/> instance
    /// </summary>
    public virtual void Initialize()
    {
        // if (SerializeMethod?.Method.IsStatic == true)
        if (SerializeMethod != null) // TODO: test that the constructor doesn't have to be static?
        {
            BaseLibMain.Logger.Info($"Registering CustomReward serializer for {GetType()}");
            CustomRewardPatches.RegisterCustomReward(RewardType, SerializeMethod);
        }
        else if (SerializeMethod != null)
        {
            throw new FieldAccessException($"Custom Reward {GetType()} has assigned a non-static method to SerializeMethod property");
        }
        else
        {
            throw new NotImplementedException($"Custom Reward {GetType()} has not implemented an Initialize() method to register a serializer for itself");
        }
    }

    // TODO: per-mod id prefixing for localisation?
}

