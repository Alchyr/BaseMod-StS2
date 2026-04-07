using BaseLib.Abstracts;
using BaseLib.Common.Rewards;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(RewardSynchronizer))]
public static class RewardSynchronizerExtensions
{
    /// <summary>
    /// Struct to save a custom reward message until combat ends
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

    extension(RewardSynchronizer rewardSynchronizer)
    {

        internal List<BufferedCustomRewardMessage> BufferedCustomRewardMessages { get { return _bufferedCustomRewardMessages; } }

        public void BufferCustomRewardMessage(CustomRewardMessage message, ulong senderId)
        {
            var bufferedMessage = new BufferedCustomRewardMessage
            {
                senderId = senderId,
                message = message
            };
            rewardSynchronizer.BufferedCustomRewardMessages.Add(bufferedMessage);
        }

        /// <summary>
        /// Exposes the private LocalPlayer property from <seealso cref="RewardSynchronizer"/>
        /// </summary>
        public Player? LocalPlayerRef => rewardSynchronizer._playerCollection.GetPlayer(rewardSynchronizer._localPlayerId);
        /// <summary>
        /// Exposes the private IPlayerCollection property
        /// </summary>
        public IPlayerCollection? PlayerCollection => rewardSynchronizer._playerCollection;
        /// <summary>
        /// Exposes the private RunLocationTargetedMessageBuffer property
        /// </summary>
        public RunLocationTargetedMessageBuffer? MessageBuffer => rewardSynchronizer._messageBuffer;
        /// <summary>
        /// Exposes the private INetGameService property
        /// </summary>
        public INetGameService? GameService => rewardSynchronizer._gameService;


        /// <summary>
        /// Method to handle transforming a card as a combat reward
        /// </summary>
        public async Task<bool> DoLocalCardTransform(bool upgrade = false)
        {
            CardTransformRewardMessage message = new CardTransformRewardMessage
            {
                location = rewardSynchronizer.MessageBuffer.CurrentLocation,
                rewardType = CardTransformReward.CardTransform,
                Upgrade = upgrade,

            };
            rewardSynchronizer.GameService?.SendMessage(message);
            return await rewardSynchronizer.DoCardTransform(rewardSynchronizer.LocalPlayerRef, upgrade);
        }

        public async Task<bool> DoCardTransform(Player player, bool upgrade = false)
        {
            CardSelectorPrefs prefs = new CardSelectorPrefs(new LocString("gameplay_ui", "COMBAT_REWARD_CARD_REMOVAL.selection_screen_prompt"), 1)
            {
                Cancelable = false,
                RequireManualConfirmation = true
            };
            CardModel? card = (await CardSelectCmd.FromDeckForTransformation(player, prefs)).FirstOrDefault();
            if (card != null)
            {
                CardModel newCard = CardFactory.CreateRandomCardForTransform(card, isInCombat: false, player.RunState.Rng.Niche);
                if (upgrade)
                {
                    newCard.UpgradeInternal();
                }
                await CardCmd.Transform(card, newCard);
                BaseLibMain.Logger.Debug($"Player {player.NetId} transformed {card.Id} in their deck into {newCard.Id}" + (upgrade ? " and upgraded it." : "."));
                return true;
            }
            return false;
        }
    }



    internal static readonly List<Type> _rewardMessageCache = [..ReflectionHelper.GetSubtypesInMods<CustomRewardMessage>()];

    [HarmonyPatch(MethodType.Constructor, [typeof(RunLocationTargetedMessageBuffer), typeof(INetGameService), typeof(IPlayerCollection), typeof(ulong)])]
    [HarmonyPostfix]
    public static void InitializeCustomRewardHandlers(RewardSynchronizer __instance)
    {
        foreach (var rewardMessageType in _rewardMessageCache)
        {
            if (rewardMessageType.CreateInstance() is not CustomRewardMessage dummyMessage)
            {
                BaseLibMain.Logger.Error($"Message instance creation for type {rewardMessageType.GetType()} from {rewardMessageType.Assembly} failed during Initialize");
                continue;
            }
            dummyMessage.Initialize(__instance.MessageBuffer);
        }
    }

    [HarmonyPatch(nameof(RewardSynchronizer.Dispose))]
    [HarmonyPostfix]
    public static void UnregisterCustomRewardHandlers(RewardSynchronizer __instance)
    {
        foreach (var rewardMessageType in _rewardMessageCache)
        {
            if (rewardMessageType.CreateInstance() is not CustomRewardMessage dummyMessage)
            {
                BaseLibMain.Logger.Error($"Message instance creation for type {rewardMessageType.GetType()} from {rewardMessageType.Assembly} failed during Dispose");
                continue;
            }
            dummyMessage.Dispose(__instance.MessageBuffer);
        }
    }

    [HarmonyPatch(nameof(RewardSynchronizer.OnCombatEnded))]
    [HarmonyPrefix]
    public static void HandleCustomBufferedMessages(RewardSynchronizer __instance)
    {
        foreach (BufferedCustomRewardMessage bufferedMessage in __instance.BufferedCustomRewardMessages)
        {
            // TODO: Get reference to appropriate message handler from the message type? Call handle method?
            // bufferedMessage.message.MessageHandler(bufferedMessage.message, bufferedMessage.senderId);
        }
        __instance.BufferedCustomRewardMessages.Clear();
    }
}
