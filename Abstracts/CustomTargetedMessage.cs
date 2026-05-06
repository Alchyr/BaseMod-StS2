using BaseLib.Extensions;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

public sealed class CustomTargetedMessageWrapper() : IRunLocationTargetedMessage, INetMessage
{
    public static byte WrapperMessageId
    {
        get;
        set;
    }
    
    public required ICustomTargetedMessage Message;
    
    private static List<Type>? _targetMessages;
    public static List<Type> TargetedMessages
    {
        get
        {
            if (_targetMessages == null)
            {
                _targetMessages = [..ReflectionHelper.GetSubtypesInMods<ICustomTargetedMessage>()];
                _targetMessages.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
            }
            return _targetMessages;
        }
    }

    private static readonly Dictionary<Type, int> CustomMessageToId = [];
    private static readonly Dictionary<int, Type> IdToCustomMessage = [];

    public static void Initialize()
    {
        foreach (var msg in TargetedMessages)
        {
            var key = (msg.FullName ?? msg.Name).ComputeBasicHash();
            while (IdToCustomMessage.TryGetValue(key, out var collision))
            {
                BaseLibMain.Logger.Warn($"Message key hash collision: {collision} and {msg} with key {key}");
                ++key;
            }
            IdToCustomMessage[key] = msg;
            CustomMessageToId[msg] = key;
        }
    }
    
    internal static void Register(RunLocationTargetedMessageBuffer messageBuffer)
    {
        messageBuffer.RegisterMessageHandler<CustomTargetedMessageWrapper>(HandleCustomMessage);
    }
    internal static void Unregister(RunLocationTargetedMessageBuffer messageBuffer)
    {
        messageBuffer.UnregisterMessageHandler<CustomTargetedMessageWrapper>(HandleCustomMessage);
    }

    private static void HandleCustomMessage(CustomTargetedMessageWrapper message, ulong senderId)
    {
        var rs = RunManager.Instance.RewardSynchronizer;
        if (CombatManager.Instance.IsInProgress && message.Message.IsRewardMessage)
        {
            rs.BufferCustomRewardMessage(message, senderId);
            BaseLibMain.Logger.Debug($"Buffered {message.Message.GetType()} message");
            return;
        }

        message.Message.HandleMessage();
    }


    public int MessageType => CustomMessageToId[Message.GetType()];
    
    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(MessageType);
        Message.Serialize(writer);
    }

    public void Deserialize(PacketReader reader)
    {
        int messageType = reader.ReadInt();
        Type msgType = IdToCustomMessage[messageType];
        Message = (ICustomTargetedMessage) Activator.CreateInstance(msgType)!;
        Message.Deserialize(reader);
    }

    public RunLocation Location => Message.Location;
    public bool ShouldBroadcast => Message.ShouldBroadcast;
    public NetTransferMode Mode => Message.Mode;
    public LogLevel LogLevel => Message.LogLevel;
    
    /// <summary>
    /// Convenience method for sending messages.
    /// </summary>
    public static void Send(ICustomTargetedMessage msg, INetGameService? netService = null)
    {
        (netService ?? RunManager.Instance.NetService).SendMessage(new CustomTargetedMessageWrapper { Message = msg });
    }
}

/// <summary>
/// A custom message that is sent using CustomTargetedMessageWrapper,
/// is processed through the targeted message buffer (occurs in a specific location in run),
/// and doesn't directly implement INetMessage.
/// </summary>
public interface ICustomTargetedMessage : IPacketSerializable
{
    /// <summary>
    /// Handle this message when it is received.
    /// Generally this means doing whatever this message says should happen, usually through
    /// <see cref="TaskHelper.RunSafely"/>.
    /// </summary>
    void HandleMessage();
    
    /// <summary>
    /// For a message sent by claiming a reward item.
    /// Will be buffered through RewardSynchronizer if true.
    /// </summary>
    bool IsRewardMessage { get; }
    
    /// <summary>
    /// The <see cref="RunLocation"/> during the run that this message was sent
    /// </summary>
    RunLocation Location { get; }
    
    /// <summary>
    /// A message that when sent to host, will be passed on to other players.
    /// </summary>
    bool ShouldBroadcast { get; }

    /// <summary>
    /// Method of message transfer.
    /// Override to Unreliable only for purely visual effects, such as communicating what a player is hovering.
    /// </summary>
    NetTransferMode Mode => NetTransferMode.Reliable;

    LogLevel LogLevel => LogLevel.VeryDebug;
}