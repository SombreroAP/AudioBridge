namespace AudioBridge.Core;

/// <summary>PCM format shared by both ends of a stream. Negotiated once at pairing time.</summary>
public sealed record AudioFormat(int SampleRate, int Channels, int BitsPerSample)
{
    /// <summary>What we default to: 48 kHz stereo 16-bit. Matches what Windows mixes at,
    /// so neither end has to resample in the hot path.</summary>
    public static readonly AudioFormat Default = new(48_000, 2, 16);

    /// <summary>Mono capture for the microphone direction.</summary>
    public static readonly AudioFormat Mono48 = new(48_000, 1, 16);

    public int BytesPerFrame => Channels * (BitsPerSample / 8);
    public int BytesPerSecond => SampleRate * BytesPerFrame;

    /// <summary>Bytes needed to hold <paramref name="milliseconds"/> of audio.</summary>
    public int BytesForDuration(double milliseconds) =>
        (int)Math.Round(BytesPerSecond * milliseconds / 1000.0 / BytesPerFrame) * BytesPerFrame;

    /// <summary>Duration, in milliseconds, of a payload of <paramref name="byteCount"/> bytes.</summary>
    public double DurationOf(int byteCount) => byteCount * 1000.0 / BytesPerSecond;
}
