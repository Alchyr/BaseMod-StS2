using BaseLib.Abstracts;
using BaseLib.Extensions;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;

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
        public ulong senderId;
        /// <summary>
        /// The message being sent
        /// </summary>
        public CustomRewardMessage message;
    }

    /// <summary>
    /// Reference list of buffered messages<br/>
    /// Hopefully there is only ever one instance of <see cref="RewardSynchronizer"/> at a time on each client?
    /// </summary>
    internal static List<BufferedCustomRewardMessage> _bufferedCustomRewardMessages = [];

    internal static List<BufferedCustomRewardMessage> BufferedCustomRewardMessages(this RewardSynchronizer rewardSynchronizer) => _bufferedCustomRewardMessages;

    /// <summary>
    /// Add a <see cref="CustomRewardMessage"/> to the combat buffer
    /// </summary>
    public static void BufferCustomRewardMessage(this RewardSynchronizer rewardSynchronizer, CustomRewardMessage message, ulong senderId)
    {
        var bufferedMessage = new BufferedCustomRewardMessage
        {
            senderId = senderId,
            message = message
        };
        rewardSynchronizer.BufferedCustomRewardMessages().Add(bufferedMessage);
    }

    /// <summary>
    /// Exposes the private LocalPlayer property from <seealso cref="RewardSynchronizer"/>
    /// </summary>
    public static Player? LocalPlayerRef(this RewardSynchronizer rewardSynchronizer) => rewardSynchronizer._playerCollection.GetPlayer(rewardSynchronizer._localPlayerId);
    /// <summary>
    /// Exposes the private IPlayerCollection property
    /// </summary>
    public static IPlayerCollection? PlayerCollection(this RewardSynchronizer rewardSynchronizer) => rewardSynchronizer._playerCollection;
    /// <summary>
    /// Exposes the private RunLocationTargetedMessageBuffer property
    /// </summary>
    public static RunLocationTargetedMessageBuffer? MessageBuffer(this RewardSynchronizer rewardSynchronizer)  => rewardSynchronizer._messageBuffer;
    /// <summary>
    /// Exposes the private INetGameService property
    /// </summary>
    public static INetGameService? GameService(this RewardSynchronizer rewardSynchronizer)  => rewardSynchronizer._gameService;

    [HarmonyPatch(nameof(RewardSynchronizer.OnCombatEnded))]
    [HarmonyPrefix]
    private static void OnCombat_HandleCustomBufferedMessages(RewardSynchronizer __instance)
    {
        foreach (BufferedCustomRewardMessage bufferedMessage in __instance.BufferedCustomRewardMessages())
        {
            __instance.MessageBuffer()?.CallHandlersOfType(bufferedMessage.message.GetType(), bufferedMessage.message, bufferedMessage.senderId);
        }
        __instance.BufferedCustomRewardMessages().Clear();
    }
}
