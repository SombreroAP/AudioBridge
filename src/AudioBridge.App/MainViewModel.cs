using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AudioBridge.Core;
using AudioBridge.Windows;

namespace AudioBridge.App;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly VbCableDevice _virtualMic = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _statusTimer;

    private PeerDiscovery? _discovery;
    private BridgeSession? _session;

    private PeerRole _role;
    private Peer? _selectedPeer;
    private AudioDeviceInfo? _selectedRenderDevice;
    private AudioDeviceInfo? _selectedCaptureDevice;
    private string _manualAddress = "";
    private string _status = "Starting up…";
    private string? _error;
    private bool _isRunning;

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Log.Write($"Loading settings from {AppSettings.FilePath}");
        _settings = AppSettings.Load();
        _role = _settings.Role;
        _manualAddress = _settings.LastPeerAddress ?? "";

        // Commands first: refreshing devices raises CanExecuteChanged on them, so they have
        // to exist before anything that can touch them runs.
        StartCommand = new RelayCommand(async void () => await StartAsync(), () => !IsRunning && Role != PeerRole.Unconfigured);
        StopCommand = new RelayCommand(async void () => await StopAsync(), () => IsRunning);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        RecheckCableCommand = new RelayCommand(RecheckCable);
        AllowFirewallCommand = new RelayCommand(AllowFirewall);

        Log.Write("Enumerating audio devices.");
        RefreshDevices();
        Log.Write($"Found {RenderDevices.Count} playback and {CaptureDevices.Count} recording devices.");
        _selectedRenderDevice = RenderDevices.FirstOrDefault(d => d.Id == _settings.RenderDeviceId)
                                ?? RenderDevices.FirstOrDefault(d => d.IsDefault);
        _selectedCaptureDevice = CaptureDevices.FirstOrDefault(d => d.Id == _settings.CaptureDeviceId)
                                 ?? CaptureDevices.FirstOrDefault(d => d.IsDefault);

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        StartDiscovery();
    }

    public ObservableCollection<Peer> Peers { get; } = [];
    public ObservableCollection<AudioDeviceInfo> RenderDevices { get; } = [];
    public ObservableCollection<AudioDeviceInfo> CaptureDevices { get; } = [];

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RefreshDevicesCommand { get; }
    public RelayCommand RecheckCableCommand { get; }
    public RelayCommand AllowFirewallCommand { get; }

    /// <summary>Shown so the user knows what to type on the other PC when discovery is blocked.</summary>
    public string LocalAddresses
    {
        get
        {
            var addresses = NetworkTargets.LocalAddresses().Select(a => a.ToString()).Distinct().ToList();
            return addresses.Count == 0 ? "(no network)" : string.Join(", ", addresses);
        }
    }

    public string CableAttribution => _virtualMic.Attribution;
    public string CableInstallUri => _virtualMic.InstallUri.ToString();

    public PeerRole Role
    {
        get => _role;
        set
        {
            if (!Set(ref _role, value)) return;
            _settings.Role = value;
            _settings.Save();
            if (_discovery is not null) _discovery.Role = value;
            NotifyRoleDependentState();
        }
    }

    public bool IsGamingPc
    {
        get => Role == PeerRole.GamingPc;
        set { if (value) Role = PeerRole.GamingPc; }
    }

    public bool IsStreamingPc
    {
        get => Role == PeerRole.StreamingPc;
        set { if (value) Role = PeerRole.StreamingPc; }
    }

    /// <summary>The gaming PC is the only side that needs VB-CABLE, because it is the side
    /// that has to present the incoming audio as a microphone.</summary>
    public bool NeedsCable => Role == PeerRole.GamingPc;

    public bool CableMissing => NeedsCable && !_virtualMic.IsAvailable;

    public bool CableReady => NeedsCable && _virtualMic.IsAvailable;

    public string CableStatus => _virtualMic.IsAvailable
        ? $"Found: {_virtualMic.InputEndpoint!.Name}. In your game, choose \"{_virtualMic.OutputEndpoint!.Name}\" as your microphone."
        : "Not installed on this PC.";

    /// <summary>The gaming PC captures a playback device (loopback); the streaming PC captures a mic.</summary>
    public string DeviceSectionLabel => Role == PeerRole.GamingPc
        ? "Capture game audio from this playback device"
        : "Play game audio to these headphones";

    public bool ShowCaptureDevice => Role == PeerRole.StreamingPc;

    public Peer? SelectedPeer
    {
        get => _selectedPeer;
        set { if (Set(ref _selectedPeer, value)) StartCommand?.RaiseCanExecuteChanged(); }
    }

    public AudioDeviceInfo? SelectedRenderDevice
    {
        get => _selectedRenderDevice;
        set
        {
            if (!Set(ref _selectedRenderDevice, value)) return;
            _settings.RenderDeviceId = value?.Id;
            _settings.Save();
        }
    }

    public AudioDeviceInfo? SelectedCaptureDevice
    {
        get => _selectedCaptureDevice;
        set
        {
            if (!Set(ref _selectedCaptureDevice, value)) return;
            _settings.CaptureDeviceId = value?.Id;
            _settings.Save();
        }
    }

    /// <summary>Typed IP for the case discovery can't cross the network (different subnets,
    /// or broadcast blocked by a firewall or VPN).</summary>
    public string ManualAddress
    {
        get => _manualAddress;
        set => Set(ref _manualAddress, value);
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string? Error
    {
        get => _error;
        private set { if (Set(ref _error, value)) Notify(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            Notify(nameof(IsIdle));
            StartCommand?.RaiseCanExecuteChanged();
            StopCommand?.RaiseCanExecuteChanged();
        }
    }

    public bool IsIdle => !IsRunning;

    private void StartDiscovery()
    {
        try
        {
            _discovery = new PeerDiscovery(_settings.InstanceId, Environment.MachineName, 47810, Role);
            _discovery.PeerDiscovered += peer => _dispatcher.Invoke(() =>
            {
                var existing = Peers.FirstOrDefault(p => p.InstanceId == peer.InstanceId);
                if (existing is not null) Peers.Remove(existing);
                Peers.Add(peer);
                SelectedPeer ??= peer;
            });
            _discovery.PeerLost += peer => _dispatcher.Invoke(() =>
            {
                var existing = Peers.FirstOrDefault(p => p.InstanceId == peer.InstanceId);
                if (existing is not null) Peers.Remove(existing);
                if (SelectedPeer?.InstanceId == peer.InstanceId) SelectedPeer = Peers.FirstOrDefault();
            });
            _discovery.Start();
            Status = "Looking for the other PC…";
        }
        catch (Exception ex)
        {
            // Almost always the firewall or another instance holding the port. The app is
            // still usable with a manually typed address, so don't treat this as fatal.
            Log.Write("Discovery unavailable", ex);
            Error = $"Automatic discovery is unavailable ({ex.Message}). Type the other PC's IP address instead.";
        }
    }

    /// <summary>Resolves where to send audio: the peer picked from discovery, or a typed address.</summary>
    private IPEndPoint? ResolveDestination()
    {
        if (!string.IsNullOrWhiteSpace(ManualAddress))
        {
            var text = ManualAddress.Trim();
            var port = 47810;
            if (text.Contains(':') && text.LastIndexOf(':') > text.LastIndexOf(']'))
            {
                var split = text.LastIndexOf(':');
                if (int.TryParse(text[(split + 1)..], out var parsedPort))
                {
                    port = parsedPort;
                    text = text[..split];
                }
            }
            return IPAddress.TryParse(text, out var address) ? new IPEndPoint(address, port) : null;
        }

        return SelectedPeer?.AudioEndPoint;
    }

    private async Task StartAsync()
    {
        Error = null;
        var destination = ResolveDestination();
        if (destination is null)
        {
            Error = "Pick the other PC from the list, or type its IP address.";
            return;
        }

        try
        {
            _settings.LastPeerAddress = string.IsNullOrWhiteSpace(ManualAddress) ? null : ManualAddress.Trim();
            _settings.Save();

            _session = new BridgeSession(_settings.ToBridgeSettings(), _virtualMic);
            _session.Failed += message => _dispatcher.Invoke(() => Error = message);
            await _session.StartAsync(destination);
            IsRunning = true;
        }
        catch (Exception ex)
        {
            Log.Write("Failed to start the bridge", ex);
            Error = ex.Message;
            if (_session is not null) await _session.DisposeAsync();
            _session = null;
        }
    }

    private async Task StopAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }
        IsRunning = false;
        Status = "Stopped.";
    }

    private void UpdateStatus()
    {
        if (_session is null || !IsRunning)
        {
            Status = Peers.Count > 0
                ? $"Found {Peers.Count} PC{(Peers.Count == 1 ? "" : "s")} on your network."
                : "Looking for the other PC…";
            return;
        }

        var status = _session.GetStatus();
        var buffered = status.PlaybackBuffered.TotalMilliseconds;
        Status = $"Connected to {status.Peer}  •  sent {status.PacketsSent:N0}  •  received {status.PacketsReceived:N0}  " +
                 $"•  buffer {buffered:F0} ms  •  {status.Jitter.ConcealedPackets:N0} dropouts";
    }

    /// <summary>
    /// Set AUDIOBRIDGE_DEMO=1 to populate the lists with sample entries. A build machine has
    /// no sound card and no peers, so without this there is nothing on screen to review and
    /// UI changes ship unseen.
    /// </summary>
    private static bool DemoMode => Environment.GetEnvironmentVariable("AUDIOBRIDGE_DEMO") == "1";

    private void AddDemoContent()
    {
        RenderDevices.Add(new AudioDeviceInfo("demo-1", "Speakers (Realtek(R) Audio)", true));
        RenderDevices.Add(new AudioDeviceInfo("demo-2", "HyperX Cloud II (USB Audio)", false));
        RenderDevices.Add(new AudioDeviceInfo("demo-3", "CABLE Input (VB-Audio Virtual Cable)", false));
        CaptureDevices.Add(new AudioDeviceInfo("demo-4", "Shure SM7B (Scarlett Solo USB)", true));
        CaptureDevices.Add(new AudioDeviceInfo("demo-5", "Microphone (HyperX Cloud II)", false));
        Peers.Add(new Peer(Guid.NewGuid(), "STREAM-PC", new IPEndPoint(IPAddress.Parse("192.168.10.42"), 47810), PeerRole.StreamingPc));
        Peers.Add(new Peer(Guid.NewGuid(), "GAMING-RIG", new IPEndPoint(IPAddress.Parse("192.168.10.17"), 47810), PeerRole.GamingPc));
    }

    private void RefreshDevices()
    {
        try
        {
            RenderDevices.Clear();
            foreach (var device in WindowsAudioDevices.GetRenderDevices()) RenderDevices.Add(device);
            CaptureDevices.Clear();
            foreach (var device in WindowsAudioDevices.GetCaptureDevices()) CaptureDevices.Add(device);
            if (DemoMode) AddDemoContent();
            RecheckCable();
        }
        catch (Exception ex)
        {
            // An unusual audio setup shouldn't stop the app opening; the user can retry.
            Log.Write("Device refresh failed", ex);
            Error = $"Could not load the audio device list: {ex.GetType().Name}: {ex.Message}. " +
                    $"Details in {Log.FilePath}";
        }
    }

    private void AllowFirewall()
    {
        var added = FirewallRule.Add(47810, PeerDiscovery.DiscoveryPort, out var message);
        Log.Write($"Firewall rule: {message}");
        Status = message;
        if (added) Error = null;
    }

    private void RecheckCable()
    {
        _virtualMic.Refresh();
        NotifyRoleDependentState();
    }

    private void NotifyRoleDependentState()
    {
        foreach (var property in new[]
                 {
                     nameof(IsGamingPc), nameof(IsStreamingPc), nameof(NeedsCable), nameof(CableMissing),
                     nameof(CableReady), nameof(CableStatus), nameof(DeviceSectionLabel), nameof(ShowCaptureDevice),
                 })
            Notify(property);
        StartCommand?.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(propertyName);
        return true;
    }

    private void Notify(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public async ValueTask DisposeAsync()
    {
        _statusTimer.Stop();
        await StopAsync();
        if (_discovery is not null) await _discovery.DisposeAsync();
    }
}
