using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;


public abstract class CustomTargetedMessage : INetMessage, IRunLocationTargetedMessage, ICustomMessage
{
    public abstract RunLocation Location { get; set; }

    public abstract bool ShouldBroadcast { get; }
    public abstract NetTransferMode Mode { get; }
    public abstract LogLevel LogLevel { get; }

    public abstract void Initialize(RunLocationTargetedMessageBuffer messageBuffer);

    public abstract void Dispose(RunLocationTargetedMessageBuffer messageBuffer);

    public abstract void Serialize(PacketWriter writer);
    public abstract void Deserialize(PacketReader reader);
}
