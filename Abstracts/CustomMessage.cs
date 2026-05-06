using BaseLib.Extensions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

public sealed class CustomMessageWrapper : INetMessage
{
    public static byte WrapperMessageId
    {
        get;
        set;
    }
    
    public required ICustomMessage Message;
    
    private static List<Type>? _customMessages;
    public static List<Type> CustomMessages
    {
        get
        {
            if (_customMessages == null)
            {
                _customMessages = [..ReflectionHelper.GetSubtypesInMods<ICustomMessage>()];
                _customMessages.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
            }
            return _customMessages;
        }
    }

    private static readonly Dictionary<Type, int> CustomMessageToId = [];
    private static readonly Dictionary<int, Type> IdToCustomMessage = [];

    public static void Initialize()
    {
        foreach (var msg in CustomMessages)
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
    
    /// Registration is done in CustomMessagePatches
    internal static void Register(INetGameService messageBuffer)
    {
        messageBuffer.RegisterMessageHandler<CustomMessageWrapper>(HandleCustomMessage);
    }
    internal static void Unregister(INetGameService messageBuffer)
    {
        messageBuffer.UnregisterMessageHandler<CustomMessageWrapper>(HandleCustomMessage);
    }
    
    
    private static void HandleCustomMessage(CustomMessageWrapper message, ulong senderId)
    {
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
        Message = (ICustomMessage) Activator.CreateInstance(msgType)!;
        Message.Deserialize(reader);
    }

    public bool ShouldBroadcast => Message.ShouldBroadcast;
    public NetTransferMode Mode => Message.Mode;
    public LogLevel LogLevel => Message.LogLevel;
    
    /// <summary>
    /// Convenience method for sending messages.
    /// </summary>
    public static void Send(ICustomMessage msg, INetGameService? netService = null)
    {
        (netService ?? RunManager.Instance.NetService).SendMessage(new CustomMessageWrapper { Message = msg });
    }
}

/// <summary>
/// A custom message that is sent using CustomMessageWrapper,
/// and doesn't directly implement INetMessage.
/// </summary>
public interface ICustomMessage : IPacketSerializable
{
    /// <summary>
    /// Handle this message when it is received.
    /// Generally this means doing whatever this message says should happen. If out of combat, usually through
    /// <see cref="TaskHelper.RunSafely"/>. Otherwise, it depends. Look at the various Synchronizer classes
    /// to see how they handle messages.
    /// </summary>
    void HandleMessage();
    
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
