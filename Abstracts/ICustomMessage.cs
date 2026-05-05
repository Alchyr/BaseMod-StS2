using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace BaseLib.Abstracts;

/// <summary>
/// Currently doesn't do anything aside from add all custom messages to a private, unused list.\n
/// Potentially useful to create optional messages in future?
/// </summary>
public interface ICustomMessage : INetMessage;
