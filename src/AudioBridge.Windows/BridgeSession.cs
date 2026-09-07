using System.Net;
using AudioBridge.Core;
using NAudio.CoreAudioApi;

namespace AudioBridge.Windows;

public sealed record BridgeSettings
{
    public PeerRole Role { get; init; } = PeerRole.Unconfigured;

    /// <summary>Playback device to capture from on the gaming PC (loopback), or to play to on
    /// the streaming PC. Null means the Windows default.</summary>
    public string? RenderDeviceId { get; init; }

    /// <summary>Microphone to capture on the streaming PC. Null means the Windows default.</summary>
    public string? CaptureDeviceId { get; init; }

    /// <summary>Packets held back before playback starts. The stability-vs-latency knob.</summary>
    public int JitterDepth { get; init; } = 3;

    /// <summary>WASAPI playback buffer, in milliseconds.</summary>
    public int RenderLatencyMs { get; init; } = 25;

    /// <summary>Audio capture block size. Kept small enough to fit one datagram.</summary>
    public double BlockMilliseconds { get; init; } = 5;

    /// <summary>Ceiling on queued playback. Sender and receiver clocks are never exactly
    /// equal, so without trimming, latency creeps upward across a long session.</summary>
    public int MaxPlaybackBufferMs { get; init; } = 75;

    public ushort AudioPort { get; init; } = 47810;
}

/// <summary>
/// One running link to the other PC. Owns the capture, the sockets, and the playback, and
/// keeps them fed.
///
/// Both roles run the same machinery pointed at different devices — the only asymmetry is
/// which stream each side sends and where the received audio is played.
/// </summary>
public sealed class BridgeSession : IAsyncDisposable
{
    private readonly BridgeSettings _settings;
    private readonly AudioFormat _format = AudioFormat.Default;
    private readonly VbCableDevice _virtualMic;

    private AudioReceiver? _receiver;
    private AudioSender? _sender;
    private WasapiCaptureSource? _capture;
    private IAudioRenderSink? _render;
    private Thread? _pump;
    private volatile bool _running;
    private byte[] _silence = [];

    public BridgeSession(BridgeSettings settings, VbCableDevice virtualMic)
    {
        _settings = settings;
        _virtualMic = virtualMic;
    }

    /// <summary>Which stream this PC sends.</summary>
    public StreamId OutboundStream => _settings.Role == PeerRole.GamingPc ? StreamId.GameAudio : StreamId.Microphone;

    /// <summary>Which stream this PC plays.</summary>
    public StreamId InboundStream => _settings.Role == PeerRole.GamingPc ? StreamId.Microphone : StreamId.GameAudio;

    public bool IsRunning => _running;
    public IPEndPoint? Peer { get; private set; }

    /// <summary>Blocks dropped to stop playback latency drifting upward.</summary>
    public long TrimmedBlocks { get; private set; }

    public event Action<string>? Failed;

    public async Task StartAsync(IPEndPoint peer, CancellationToken cancellationToken = default)
    {
        if (_running) throw new InvalidOperationException("Session already running.");
        if (_settings.Role == PeerRole.Unconfigured)
            throw new InvalidOperationException("Pick which PC this is before starting.");

        Peer = peer;
        _silence = new byte[_format.BytesForDuration(_settings.BlockMilliseconds)];

        _receiver = new AudioReceiver(_settings.AudioPort, _settings.JitterDepth);
        _receiver.Start();
        _sender = new AudioSender(peer, _format);

        _render = OpenRenderSink();
        await _render.StartAsync(cancellationToken);

        _capture = OpenCapture();
        _capture.DataAvailable += OnCaptured;
        await _capture.StartAsync(cancellationToken);

        _running = true;
        // A dedicated thread, not the thread pool: this loop must keep the sound card fed on
        // a steady cadence, and pool threads can be delayed by unrelated work.
        _pump = new Thread(PumpLoop) { IsBackground = true, Name = "AudioBridge render pump", Priority = ThreadPriority.AboveNormal };
        _pump.Start();
    }

    private IAudioRenderSink OpenRenderSink()
    {
        // The gaming PC plays the incoming microphone into VB-CABLE so games see it as a mic.
        // The streaming PC plays incoming game audio out of the user's headphones.
        if (_settings.Role == PeerRole.GamingPc)
        {
            if (!_virtualMic.IsAvailable)
                throw new InvalidOperationException(
                    "VB-CABLE is not installed on this PC, so the microphone cannot be received. " +
                    "Install it from vb-cable.com, or run this PC in send-only mode.");
            return _virtualMic.OpenSink(_format);
        }

        var device = WindowsAudioDevices.Resolve(_settings.RenderDeviceId, DataFlow.Render)
            ?? throw new InvalidOperationException("No playback device available.");
        return new WasapiRenderSink(device, _format, _settings.RenderLatencyMs);
    }

    private WasapiCaptureSource OpenCapture()
    {
        if (_settings.Role == PeerRole.GamingPc)
        {
            // Loopback-record the device the games are playing through.
            var device = WindowsAudioDevices.Resolve(_settings.RenderDeviceId, DataFlow.Render)
                ?? throw new InvalidOperationException("No playback device to capture game audio from.");

            // Installing VB-CABLE often makes CABLE Input the default playback device, so
            // this is the setup a user lands on by accident. Capturing the device we render
            // the incoming microphone into feeds it straight back to the other PC, which is
            // heard as your own voice returning on a delay.
            if (_virtualMic.InputEndpoint is not null && device.ID == _virtualMic.InputEndpoint.Id)
            {
                var name = device.FriendlyName;
                device.Dispose();
                throw new InvalidOperationException(
                    $"\"{name}\" cannot be the device game audio is captured from -- it is the " +
                    "virtual microphone AudioBridge plays into, so the audio would loop straight " +
                    "back. Pick the speakers or headset your games actually play through.");
            }

            return WasapiCaptureSource.Loopback(device, _format, _settings.BlockMilliseconds);
        }

        var mic = WindowsAudioDevices.Resolve(_settings.CaptureDeviceId, DataFlow.Capture)
            ?? throw new InvalidOperationException("No microphone available.");
        return WasapiCaptureSource.Microphone(mic, _format, _settings.BlockMilliseconds);
    }

    private void OnCaptured(ReadOnlyMemory<byte> block)
    {
        try
        {
            _sender?.Send(OutboundStream, block.Span);
        }
        catch (Exception ex)
        {
            // Never let an exception escape into the WASAPI capture thread; it would kill
            // capture for the rest of the session with no way to recover.
            Failed?.Invoke(ex.Message);
        }
    }

    private void PumpLoop()
    {
        while (_running)
        {
            var wroteSomething = false;
            while (_receiver!.TryRead(InboundStream, out var packet))
            {
                wroteSomething = true;

                // The two PCs' sound cards run at slightly different real rates, so audio
                // arrives fractionally faster than it plays and the backlog grows all
                // session. Dropping a block once the queue is too deep holds latency at the
                // profile's ceiling instead of letting it drift into seconds.
                if (_render is WasapiRenderSink sink &&
                    sink.BufferedDuration.TotalMilliseconds > _settings.MaxPlaybackBufferMs)
                {
                    TrimmedBlocks++;
                    continue;
                }

                // A concealed packet carries no payload: play a block of silence so the
                // stream keeps its timing instead of jumping forward.
                _render!.Write(packet.Payload.IsEmpty ? _silence : packet.Payload.Span);
            }

            // Sleep only when the buffer ran dry, so a burst of packets drains in one pass.
            if (!wroteSomething) Thread.Sleep(2);
        }
    }

    public BridgeStatus GetStatus()
    {
        var stats = _receiver?.GetStats(InboundStream) ?? default;
        return new BridgeStatus(
            _running,
            Peer,
            _sender?.PacketsSent ?? 0,
            _receiver?.PacketsReceived ?? 0,
            stats,
            (_render as WasapiRenderSink)?.BufferedDuration ?? TimeSpan.Zero,
            TrimmedBlocks);
    }

    public async Task StopAsync()
    {
        _running = false;
        _pump?.Join(TimeSpan.FromSeconds(1));
        _pump = null;

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnCaptured;
            await _capture.DisposeAsync();
            _capture = null;
        }

        if (_render is not null)
        {
            await _render.StopAsync();
            await _render.DisposeAsync();
            _render = null;
        }

        _sender?.Dispose();
        _sender = null;

        if (_receiver is not null)
        {
            await _receiver.DisposeAsync();
            _receiver = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

public readonly record struct BridgeStatus(
    bool IsRunning,
    IPEndPoint? Peer,
    long PacketsSent,
    long PacketsReceived,
    JitterBufferStats Jitter,
    TimeSpan PlaybackBuffered,
    long TrimmedBlocks);
