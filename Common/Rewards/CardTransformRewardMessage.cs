using BaseLib.Abstracts;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Common.Rewards;

/// <summary>
/// Message for transforming a card from a new reward type
/// </summary>
public sealed class ZZ_CardTransformRewardMessage : CustomRewardMessage
{
    internal void HandleCardTransformedMessage(ZZ_CardTransformRewardMessage message, ulong senderId)
    {
        BaseLibMain.Logger.Debug($"Handling message {message}");
        var rs = RunManager.Instance.RewardSynchronizer;
        if (CombatManager.Instance.IsInProgress)
        {
            rs.BufferCustomRewardMessage(message, senderId);
            BaseLibMain.Logger.Debug($"Buffered card transform message for {rs.PlayerCollection()?.GetPlayer(senderId)}");
            return;
        }

        Player? player = rs.PlayerCollection()?.GetPlayer(senderId);
        if (player == rs.LocalPlayerRef())
        {
            throw new InvalidOperationException("CardTransformRewardMessage should not be sent to the player transforming the card");
        }
        TaskHelper.RunSafely(rs.DoCardTransform(player, message.Amount, message.Upgrade));
    }

    /// <inheritdoc/>
    public override void Dispose(RunLocationTargetedMessageBuffer messageBuffer)
    {
        BaseLibMain.Logger.Debug($"Unregistering handler for {GetType()}");
        messageBuffer.UnregisterMessageHandler<ZZ_CardTransformRewardMessage>(HandleCardTransformedMessage);
    }

    /// <inheritdoc/>
    public override void Initialize(RunLocationTargetedMessageBuffer messageBuffer)
    {
        BaseLibMain.Logger.Debug($"Registering handler for {GetType()}");
        messageBuffer.RegisterMessageHandler<ZZ_CardTransformRewardMessage>(HandleCardTransformedMessage);
    }

    /// <summary>
    /// Whether to upgrade the card as well as transforming
    /// </summary>
    public required bool Upgrade;
    /// <summary>
    /// The amount of cards to select from
    /// </summary>
    public required int Amount;

    /// <inheritdoc/>
    public override LogLevel LogLevel => LogLevel.Debug;

    /// <inheritdoc/>
    public override void Deserialize(PacketReader reader)
    {
        Location = reader.Read<RunLocation>();
        Amount = reader.ReadInt();
        Upgrade = reader.ReadBool();
    }

    /// <inheritdoc/>
    public override void Serialize(PacketWriter writer)
    {
        writer.Write(Location);
        writer.WriteInt(Amount);
        writer.WriteBool(Upgrade);
    }
}
