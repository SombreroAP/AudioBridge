namespace AudioBridge.Core;

/// <summary>Identifies which of the two directions a packet belongs to.
/// Both can be in flight at once over the same socket pair.</summary>
public enum StreamId : ushort
{
    /// <summary>Game/desktop audio captured on the gaming PC, played on the streaming PC's headphones.</summary>
    GameAudio = 1,

    /// <summary>Microphone captured on the streaming PC, injected as a mic on the gaming PC.</summary>
    Microphone = 2,
}
