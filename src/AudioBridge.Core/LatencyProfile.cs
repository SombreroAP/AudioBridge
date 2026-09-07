namespace AudioBridge.Core;

/// <summary>
/// The latency-versus-robustness trade-off, as a few named presets.
///
/// Total delay is roughly: one capture block, plus the jitter buffer, plus the playback
/// buffer. Every one of those exists to absorb a different kind of hiccup, so cutting them
/// buys responsiveness and pays for it in dropouts when the network or the CPU stutters.
/// </summary>
public sealed record LatencyProfile(
    string Name,
    double BlockMilliseconds,
    int JitterDepth,
    int RenderLatencyMs,
    int MaxPlaybackBufferMs)
{
    /// <summary>
    /// Capture blocks are sized to fit in a single datagram. A 10 ms stereo block is 1920
    /// bytes, which splits across two packets and makes the jitter buffer's depth mean
    /// something different from one block to the next; 5 ms is 960 bytes and stays whole.
    /// </summary>
    public static readonly LatencyProfile Lowest =
        new("Lowest latency", BlockMilliseconds: 5, JitterDepth: 2, RenderLatencyMs: 15, MaxPlaybackBufferMs: 45);

    public static readonly LatencyProfile Balanced =
        new("Balanced", BlockMilliseconds: 5, JitterDepth: 3, RenderLatencyMs: 25, MaxPlaybackBufferMs: 75);

    // Every profile uses 5 ms blocks -- the datagram limit caps a stereo block at about
    // 7.3 ms, so robustness is bought with jitter depth rather than with bigger blocks.
    public static readonly LatencyProfile Stable =
        new("Most stable", BlockMilliseconds: 5, JitterDepth: 8, RenderLatencyMs: 40, MaxPlaybackBufferMs: 150);

    public static IReadOnlyList<LatencyProfile> All { get; } = [Lowest, Balanced, Stable];

    public static LatencyProfile ByName(string? name) =>
        All.FirstOrDefault(profile => profile.Name == name) ?? Balanced;

    /// <summary>Rough end-to-end delay, for showing next to each option.</summary>
    public double EstimatedMilliseconds =>
        BlockMilliseconds                      // filling one capture block
        + JitterDepth * BlockMilliseconds      // waiting for the jitter buffer to prime
        + RenderLatencyMs;                     // the sound card's own buffer

    public override string ToString() => $"{Name}  (~{EstimatedMilliseconds:F0} ms)";
}
