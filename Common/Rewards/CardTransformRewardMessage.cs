using BaseLib.Abstracts;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace BaseLib.Common.Rewards;

public sealed class CardTransformRewardMessage : CustomRewardMessage
{
    private void HandleCardTransformedMessage(CardTransformRewardMessage message, ulong senderId)
    {
        var rs = RunManager.Instance.RewardSynchronizer;
        if (CombatManager.Instance.IsInProgress)
        {
            rs.BufferCustomRewardMessage(message, senderId);
            return;
        }

        Player player = rs.PlayerCollection.GetPlayer(senderId);
        if (player == rs.LocalPlayer)
        {
            throw new InvalidOperationException($"CardTransformRewardMessage should not be sent to the player transforming the card");
        }
        TaskHelper.RunSafely(rs.DoCardTransform(player));
    }

    public override void Dispose(RunLocationTargetedMessageBuffer messageBuffer)
    {
        messageBuffer.UnregisterMessageHandler<CardTransformRewardMessage>(HandleCardTransformedMessage);
    }

    public override void Initialize(RunLocationTargetedMessageBuffer messageBuffer)
    {
        messageBuffer.RegisterMessageHandler<CardTransformRewardMessage>(HandleCardTransformedMessage);
    }

    public required bool Upgrade;
    public override void Deserialize(PacketReader reader)
    {
    }


    public override void Serialize(PacketWriter writer)
    {
    }
}
