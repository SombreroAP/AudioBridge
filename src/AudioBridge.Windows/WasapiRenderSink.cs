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

    /// <param name="latencyMilliseconds">WASAPI's own buffer. Below about 20 ms shared-mode
    /// playback starts to crackle on typical consumer hardware, so that is the floor the UI offers.</param>
    public WasapiRenderSink(MMDevice device, AudioFormat format, int latencyMilliseconds = 30)
    {
        _device = device;
        Format = format;

        _buffer = new BufferedWaveProvider(new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels))
        {
            // Overflow means the network is ahead of the sound card. Dropping the newest
            // audio keeps latency bounded instead of letting it creep up all session.
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(Math.Max(500, latencyMilliseconds * 4)),
        };

        _output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latencyMilliseconds);
        _output.Init(_buffer);
    }

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
