using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

/// <summary>
/// Abstract class to inherit for syncing rewards
/// </summary>
public abstract class CustomRewardMessage : CustomTargetedMessage
{
    /// <summary>
    /// Include whether the reward was skipped or not
    /// Not required to actually check, namely if being used for a per-player reward like <seealso cref="PaelsWingSacrificeMessage"/>
    /// </summary>
    public required bool wasSkipped;

    /// <summary>
    /// You probably want to broadcast the message
    /// </summary>
    public sealed override bool ShouldBroadcast => true;

    /// <summary>
    /// Rewards should probably be sent reliably too
    /// </summary>
    public sealed override NetTransferMode Mode => NetTransferMode.Reliable;

    /// <summary>
    /// Set when instantiating, afaik needed for saving to the run?
    /// </summary>
    public required override RunLocation Location { get; set; }
}
