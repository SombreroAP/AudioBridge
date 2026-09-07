using AudioBridge.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioBridge.Windows;

/// <summary>
/// Plays received audio out of a Windows endpoint — the headphones on the streaming PC, or
/// VB-CABLE's input on the gaming PC.
/// </summary>
public sealed class WasapiRenderSink : IAudioRenderSink
{
    private readonly WasapiOut _output;
    private readonly BufferedWaveProvider _buffer;
    private readonly MMDevice _device;

    /// <param name="latencyMilliseconds">WASAPI's own buffer. How low this can go is a
    /// property of the driver, not a constant, so a request the device rejects falls back
    /// rather than failing to start.</param>
    public WasapiRenderSink(MMDevice device, AudioFormat format, int latencyMilliseconds = 25)
    {
        _device = device;
        Format = format;

        _buffer = new BufferedWaveProvider(new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels))
        {
            // Overflow means the network is ahead of the sound card. Dropping the newest
            // audio keeps latency bounded instead of letting it creep up all session.
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(Math.Max(250, latencyMilliseconds * 4)),
        };

        // Try the requested latency, then progressively safer ones. Shared-mode WASAPI
        // below roughly 20 ms is fine on some hardware and refused outright on other, and
        // the only way to find out is to ask.
        foreach (var candidate in new[] { latencyMilliseconds, 30, 50, 100 }.Distinct().Order())
        {
            try
            {
                var output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, candidate);
                output.Init(_buffer);
                _output = output;
                ActualLatencyMs = candidate;
                return;
            }
            catch (Exception)
            {
                // Too aggressive for this driver; try the next one up.
            }
        }

        throw new InvalidOperationException(
            $"{device.FriendlyName} would not accept any playback buffer size AudioBridge offered.");
    }

    /// <summary>The buffer size the device actually accepted, which may be larger than asked for.</summary>
    public int ActualLatencyMs { get; }

    public AudioFormat Format { get; }

    /// <summary>How much audio is queued but not yet played. The UI shows this as latency.</summary>
    public TimeSpan BufferedDuration => _buffer.BufferedDuration;

    public void Write(ReadOnlySpan<byte> pcm) => _buffer.AddSamples(pcm.ToArray(), 0, pcm.Length);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _output.Play();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _output.Stop();
        _buffer.ClearBuffer();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _output.Dispose();
        _device.Dispose();
        return ValueTask.CompletedTask;
    }
}
