using AudioBridge.Core;

namespace AudioBridge.Core.Tests;

public class AudioFormatTests
{
    [Fact]
    public void DefaultIs48kStereo16Bit()
    {
        var format = AudioFormat.Default;
        Assert.Equal(4, format.BytesPerFrame);
        Assert.Equal(192_000, format.BytesPerSecond);
    }

    [Fact]
    public void TenMillisecondsOfStereoIs1920Bytes() =>
        Assert.Equal(1920, AudioFormat.Default.BytesForDuration(10));

    [Fact]
    public void DurationIsTheInverseOfSize()
    {
        var format = AudioFormat.Default;
        Assert.Equal(10, format.DurationOf(format.BytesForDuration(10)), precision: 6);
    }

    [Fact]
    public void BufferSizesAlwaysLandOnAFrameBoundary()
    {
        // A payload that splits a frame would desync the channels for the rest of the stream.
        foreach (var ms in new double[] { 1, 2.5, 5, 7.3, 10, 20 })
            Assert.Equal(0, AudioFormat.Default.BytesForDuration(ms) % AudioFormat.Default.BytesPerFrame);
    }
}
