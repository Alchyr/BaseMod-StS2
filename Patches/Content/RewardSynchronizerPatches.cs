using BaseLib.Abstracts;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace BaseLib.Patches.Content;

/// <summary>
/// Extensions to <see cref="RewardSynchronizer"/> to provide public getters to internal properties and common reward functions
/// </summary>
[HarmonyPatch(typeof(RewardSynchronizer))]
public static class RewardSynchronizerExtensions
{
    /// <summary>
    /// Struct to save a custom reward message until combat ends
    /// Prefer creating with <see cref="BufferCustomRewardMessage"/>
    /// </summary>
    public struct BufferedCustomRewardMessage
    {
        /// <summary>
        /// the id of the player who sent the message
        /// </summary>
        public ulong SenderId;
        /// <summary>
        /// The message being sent
        /// </summary>
        public CustomTargetedMessageWrapper Message;
    }

    // Not used in BaseLib as publicizer is enabled, but useful for mods without.
    /// <summary>
    /// Exposes the private INetGameService property.
    /// </summary>
    public static INetGameService? GameService(this RewardSynchronizer rewardSynchronizer) => rewardSynchronizer._gameService;
    
    /// <summary>
    /// Reference list of buffered messages<br/>
    /// </summary>
    internal static readonly SpireField<RewardSynchronizer, List<BufferedCustomRewardMessage>>
        BufferedCustomRewardMessages = new(() => []);

    /// <summary>
    /// Add a <see cref="CustomRewardMessage"/> to the combat buffer
    /// </summary>
    public static void BufferCustomRewardMessage(this RewardSynchronizer rewardSynchronizer, CustomTargetedMessageWrapper message, ulong senderId)
    {
        var bufferedMessage = new BufferedCustomRewardMessage
        {
            SenderId = senderId,
            Message = message
        };
        BufferedCustomRewardMessages[rewardSynchronizer]!.Add(bufferedMessage);
    }

    [HarmonyPatch(nameof(RewardSynchronizer.OnCombatEnded))]
    [HarmonyPrefix]
    private static void OnCombatEndHandleCustomBufferedMessages(RewardSynchronizer __instance)
    {
        foreach (var bufferedMessage in BufferedCustomRewardMessages[__instance]!)
        {
            __instance._messageBuffer?.CallHandlersOfType(bufferedMessage.Message.GetType(), bufferedMessage.Message, bufferedMessage.SenderId);
        }
        BufferedCustomRewardMessages[__instance]!.Clear();
    }
}
