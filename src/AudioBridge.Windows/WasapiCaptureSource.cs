using AudioBridge.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioBridge.Windows;

/// <summary>
/// Captures audio with WASAPI and hands it out in the fixed wire format.
///
/// Covers both capture jobs: <see cref="Loopback"/> grabs everything the gaming PC is
/// playing (the game, Discord, music) by recording its own output, and <see cref="Microphone"/>
/// records a physical input on the streaming PC.
/// </summary>
public sealed class WasapiCaptureSource : IAudioCaptureSource
{
    private readonly IWaveIn _capture;
    private readonly FormatConverter _converter;
    private readonly MMDevice? _device;
    private TaskCompletionSource? _stopped;

    private WasapiCaptureSource(IWaveIn capture, MMDevice? device, AudioFormat format, double blockMilliseconds)
    {
        _capture = capture;
        _device = device;
        Format = format;
        _converter = new FormatConverter(capture.WaveFormat, format, format.BytesForDuration(blockMilliseconds));

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, _) => _stopped?.TrySetResult();
    }

    public AudioFormat Format { get; }

    /// <summary>Raised on the WASAPI capture thread. Handlers must not block.</summary>
    public event Action<ReadOnlyMemory<byte>>? DataAvailable;

    /// <summary>Records what a playback device is currently outputting. This is how game
    /// audio gets captured without any virtual device on the gaming PC.</summary>
    public static WasapiCaptureSource Loopback(MMDevice renderDevice, AudioFormat format, double blockMilliseconds = 10) =>
        new(new WasapiLoopbackCapture(renderDevice), renderDevice, format, blockMilliseconds);

    /// <summary>Records a physical microphone.</summary>
    public static WasapiCaptureSource Microphone(MMDevice captureDevice, AudioFormat format, double blockMilliseconds = 10) =>
        new(new WasapiCapture(captureDevice) { ShareMode = AudioClientShareMode.Shared }, captureDevice, format, blockMilliseconds);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _capture.StartRecording();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var stopped = _stopped;
        _capture.StopRecording();
        if (stopped is not null)
        {
            // WASAPI signals RecordingStopped asynchronously; don't tear down the device
            // out from under its own callback thread.
            await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        _converter.Write(e.Buffer, 0, e.BytesRecorded);
        _converter.DrainTo(block => DataAvailable?.Invoke(block));
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); } catch { /* disposing anyway */ }
        _capture.Dispose();
        _device?.Dispose();
    }
}
