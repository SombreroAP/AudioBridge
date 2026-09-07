using AudioBridge.Core;
using NAudio.CoreAudioApi;

namespace AudioBridge.Windows;

/// <summary>Enumerates the playback and recording endpoints Windows currently has active.</summary>
public static class WindowsAudioDevices
{
    public static IReadOnlyList<AudioDeviceInfo> GetRenderDevices() => Enumerate(DataFlow.Render);

    public static IReadOnlyList<AudioDeviceInfo> GetCaptureDevices() => Enumerate(DataFlow.Capture);

    /// <summary>Resolves an id back to a live device, falling back to the system default when
    /// the saved device has been unplugged since the settings were written.</summary>
    public static MMDevice? Resolve(string? deviceId, DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrEmpty(deviceId))
        {
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                if (device.ID == deviceId) return device;
                device.Dispose();
            }
        }

        return enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia)
            ? enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia)
            : null;
    }

    private static IReadOnlyList<AudioDeviceInfo> Enumerate(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia)
            ? enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID
            : null;

        var devices = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId));
            device.Dispose();
        }
        return devices;
    }
}
