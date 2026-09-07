using System.Net;
using AudioBridge.Core;

namespace AudioBridge.Core.Tests;

public class LatencyProfileTests
{
    public static TheoryData<LatencyProfile> AllProfiles()
    {
        var data = new TheoryData<LatencyProfile>();
        foreach (var profile in LatencyProfile.All) data.Add(profile);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void ACaptureBlockFitsInASingleDatagram(LatencyProfile profile)
    {
        // If a block spans two packets, the jitter buffer's depth means a different amount
        // of audio from one block to the next, and the latency estimate stops being true.
        using var sender = new AudioSender(new IPEndPoint(IPAddress.Loopback, 1), AudioFormat.Default);
        var blockSize = AudioFormat.Default.BytesForDuration(profile.BlockMilliseconds);

        Assert.True(blockSize <= sender.MaxChunkSize,
            $"{profile.Name}: a {profile.BlockMilliseconds} ms block is {blockSize} bytes, " +
            $"over the {sender.MaxChunkSize} byte datagram limit.");
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void BlocksAreAWholeNumberOfFrames(LatencyProfile profile) =>
        Assert.Equal(0, AudioFormat.Default.BytesForDuration(profile.BlockMilliseconds) % AudioFormat.Default.BytesPerFrame);

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void ThePlaybackCeilingLeavesRoomAboveTheBufferItIsTrimming(LatencyProfile profile) =>
        // Trimming at or below the sound card's own buffer would drop audio constantly.
        Assert.True(profile.MaxPlaybackBufferMs > profile.RenderLatencyMs,
            $"{profile.Name}: trims at {profile.MaxPlaybackBufferMs} ms but the device buffer alone is {profile.RenderLatencyMs} ms.");

    [Fact]
    public void ProfilesAreOrderedFromFastestToMostForgiving()
    {
        var estimates = LatencyProfile.All.Select(p => p.EstimatedMilliseconds).ToList();
        Assert.Equal(estimates.OrderBy(e => e), estimates);
    }

    [Fact]
    public void TheFastestProfileBeatsWhatWeShippedBefore() =>
        // 0.1.3 was a 10 ms block, depth 3, 30 ms device buffer: about 70 ms.
        Assert.True(LatencyProfile.Lowest.EstimatedMilliseconds < 40,
            $"Lowest is {LatencyProfile.Lowest.EstimatedMilliseconds} ms.");

    [Fact]
    public void AnUnknownOrMissingNameFallsBackToBalanced()
    {
        Assert.Equal(LatencyProfile.Balanced, LatencyProfile.ByName(null));
        Assert.Equal(LatencyProfile.Balanced, LatencyProfile.ByName("Ludicrous"));
    }

    [Fact]
    public void NamesRoundTrip()
    {
        // Settings persist the name, so a rename must not silently reset the user's choice.
        foreach (var profile in LatencyProfile.All)
            Assert.Equal(profile, LatencyProfile.ByName(profile.Name));
    }
}
