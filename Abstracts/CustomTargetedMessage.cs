using BaseLib.Common.Rewards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

/// <summary>
/// Message class to inherit when your message is relevant to the current run state
/// </summary>
public abstract class CustomTargetedMessage : INetMessage, IRunLocationTargetedMessage, ICustomMessage
{
    /// <summary>
    /// The <see cref="RunLocation"/> during the run that this message was sent
    /// </summary>
    public abstract RunLocation Location { get; set; }

    /// <summary>
    /// Whether this message should be broadcasted to the other players
    /// </summary>
    public abstract bool ShouldBroadcast { get; }
    /// <summary>
    /// The way to transfer the message
    /// </summary>
    public abstract NetTransferMode Mode { get; }
    /// <summary>
    /// What log level to output to (referenced when calling the vanilla handler(s) for messages)
    /// </summary>
    public abstract LogLevel LogLevel { get; }

    /// <summary>
    /// Register your message type here.
    /// Needs to be a function that takes <c>(<see cref="ICustomMessage"/> message, <see langword="ulong"/> senderId)</c>
    /// See <seealso cref="ZZ_CardTransformRewardMessage.HandleCardTransformedMessage"/> for an example.
    /// You probably want to use an
    /// <see href="https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/extension-methods">Extension Method</see>
    /// </summary>
    public abstract void Initialize(RunLocationTargetedMessageBuffer messageBuffer);

    /// <summary>
    /// Unregister your message type here<br/>
    /// Reference the same function you registered in <see cref="Initialize(RunLocationTargetedMessageBuffer)"/>
    /// </summary>
    public abstract void Dispose(RunLocationTargetedMessageBuffer messageBuffer);

    /// <summary>
    /// How your message is "written" to be sent over the internet
    /// </summary>
    /// <param name="writer">The packet to write your data to</param>
    public abstract void Serialize(PacketWriter writer);

    /// <summary>
    /// Read out your message into whatever variables it was created from
    /// </summary>
    /// <param name="reader">Parameter description.</param>
    public abstract void Deserialize(PacketReader reader);
}
