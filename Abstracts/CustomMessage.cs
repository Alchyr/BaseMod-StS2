using BaseLib.Common.Rewards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace BaseLib.Abstracts;

/// <summary>
/// The type to inherit from to add a custom message.
/// Not actually necessary to inherit from, just provides some helpful abstract methods as reminders/hints for setting up a message
/// </summary>
public abstract class CustomMessage : INetMessage, ICustomMessage
{
    /// <summary>
    /// Register your message type here.
    /// Needs to be a function that takes <c>(<see cref="ICustomMessage"/> message, <see langword="ulong"/> senderId)</c>
    /// See <seealso cref="ZZ_CardTransformRewardMessage.HandleCardTransformedMessage"/> for an example.
    /// You probably want to use an <see href="https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/extension-methods">Extension Method</see>
    /// </summary>
    public abstract void Initialize(INetGameService netService);

    /// <summary>
    /// Unregister your message type here<br/>
    /// Reference the same function you registered in <see cref="Initialize(INetGameService)"/>
    /// </summary>
    public abstract void Dispose(INetGameService netService);
    /// <summary>
    /// How your message is "written" to be sent over the internet
    /// </summary>
    /// <param name="writer">The packet to write your data to</param>
    public abstract void Serialize(PacketWriter writer);
    /// <summary>
    /// Read out your message into whatever variables it was created from, in the same order as written
    /// </summary>
    /// <param name="reader">The data sent from the other client</param>
    public abstract void Deserialize(PacketReader reader);

    /// <summary>
    /// Whether to broadcast the message
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
}
