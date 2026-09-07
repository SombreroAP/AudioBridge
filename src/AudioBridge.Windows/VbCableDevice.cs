using AudioBridge.Core;
using NAudio.CoreAudioApi;

namespace AudioBridge.Windows;

/// <summary>
/// Uses VB-Audio's VB-CABLE as the virtual microphone on the gaming PC.
///
/// VB-CABLE is a loopback pair: whatever is played into "CABLE Input" comes out of
/// "CABLE Output", which Windows presents as a recording device. So AudioBridge renders the
/// incoming microphone audio into CABLE Input, and the user picks CABLE Output as their mic
/// in-game.
///
/// AudioBridge does not bundle or install VB-CABLE — see docs/licensing.md. When it is
/// missing, the UI credits VB-Audio and sends the user to <see cref="InstallUri"/>.
/// </summary>
public sealed class VbCableDevice : IVirtualMicDevice
{
    // VB-CABLE's endpoints are named "CABLE Input (VB-Audio Virtual Cable)" and
    // "CABLE Output (VB-Audio Virtual Cable)". Match on the distinctive part rather than
    // the whole string, which varies across versions and locales.
    private const string InputMatch = "CABLE Input";
    private const string OutputMatch = "CABLE Output";
    private const string VendorMatch = "VB-Audio";

    public VbCableDevice() => Refresh();

    public string ProviderName => "VB-CABLE (VB-Audio Software)";

    public Uri InstallUri { get; } = new("https://vb-cable.com");

    /// <summary>Shown next to the install link. VB-Audio's terms require the user be told the
    /// software is theirs, where it comes from, and that it is donationware.</summary>
    public string Attribution =>
        "VB-CABLE is made by VB-Audio Software and is available from vb-cable.com. " +
        "It is donationware: free to use, and VB-Audio welcomes a donation if you find it useful.";

    public AudioDeviceInfo? InputEndpoint { get; private set; }

    public AudioDeviceInfo? OutputEndpoint { get; private set; }

    /// <summary>Both halves must be present; one without the other means a broken install.</summary>
    public bool IsAvailable => InputEndpoint is not null && OutputEndpoint is not null;

    /// <summary>Re-checks for VB-CABLE. Called after the user says they have installed it,
    /// so they don't have to restart AudioBridge.</summary>
    public void Refresh()
    {
        InputEndpoint = FindEndpoint(WindowsAudioDevices.GetRenderDevices(), InputMatch);
        OutputEndpoint = FindEndpoint(WindowsAudioDevices.GetCaptureDevices(), OutputMatch);
    }

    public IAudioRenderSink OpenSink(AudioFormat format)
    {
        if (InputEndpoint is null)
            throw new InvalidOperationException("VB-CABLE is not installed; cannot open the virtual microphone.");

        var device = WindowsAudioDevices.Resolve(InputEndpoint.Id, DataFlow.Render)
            ?? throw new InvalidOperationException("VB-CABLE input disappeared between detection and use.");

        return new WasapiRenderSink(device, format);
    }

    private static AudioDeviceInfo? FindEndpoint(IReadOnlyList<AudioDeviceInfo> devices, string match)
    {
        // Prefer a device that names VB-Audio too, so a differently-named third-party cable
        // can't be mistaken for the real thing.
        return devices.FirstOrDefault(d =>
                   d.Name.Contains(match, StringComparison.OrdinalIgnoreCase) &&
                   d.Name.Contains(VendorMatch, StringComparison.OrdinalIgnoreCase))
               ?? devices.FirstOrDefault(d => d.Name.Contains(match, StringComparison.OrdinalIgnoreCase));
    }
}
