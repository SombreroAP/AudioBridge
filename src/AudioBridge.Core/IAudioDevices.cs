namespace AudioBridge.Core;

/// <summary>A playback or capture endpoint the user can pick in the UI.</summary>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

/// <summary>
/// Produces PCM audio. On the gaming PC this is a WASAPI loopback capture of the default
/// render device (the game); on the streaming PC it's the physical microphone.
/// </summary>
public interface IAudioCaptureSource : IAsyncDisposable
{
    AudioFormat Format { get; }
    /// <summary>Raised on the capture thread. Handlers must not block.</summary>
    event Action<ReadOnlyMemory<byte>>? DataAvailable;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

/// <summary>Consumes PCM audio and plays it out of a real device.</summary>
public interface IAudioRenderSink : IAsyncDisposable
{
    AudioFormat Format { get; }
    void Write(ReadOnlySpan<byte> pcm);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

/// <summary>
/// The seam that keeps us off a kernel driver for now.
///
/// To make a remote microphone appear as a real mic to games and Discord, the audio has to
/// be written into a device that Windows enumerates as a capture endpoint. Today that's
/// VB-CABLE: we render into "CABLE Input", and apps select "CABLE Output" as their mic.
/// If we ever ship our own signed WDM driver, it implements this same interface and
/// nothing above it changes.
/// </summary>
public interface IVirtualMicDevice
{
    /// <summary>Human-readable name of the backing implementation, e.g. "VB-CABLE".</summary>
    string ProviderName { get; }

    /// <summary>False when the backing device isn't installed; the UI then runs the setup step.</summary>
    bool IsAvailable { get; }

    /// <summary>The endpoint we render into. Null when unavailable.</summary>
    AudioDeviceInfo? InputEndpoint { get; }

    /// <summary>The endpoint the user selects as their microphone in games. Null when unavailable.</summary>
    AudioDeviceInfo? OutputEndpoint { get; }

    /// <summary>Where to send the user to install it, shown alongside the required attribution.</summary>
    Uri InstallUri { get; }

    /// <summary>Opens a sink that feeds <see cref="InputEndpoint"/>.</summary>
    IAudioRenderSink OpenSink(AudioFormat format);
}
