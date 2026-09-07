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

        string? defaultId = null;
        try
        {
            if (enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia))
            {
                using var defaultDevice = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
                defaultId = defaultDevice.ID;
            }
        }
        catch (Exception)
        {
            // Not having a default endpoint is survivable; the user can still pick one.
        }

        var devices = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            try
            {
                // Reading a device's name goes to its property store, which some virtual and
                // badly-behaved drivers do not populate. One such device must not take out
                // the whole list.
                devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId));
            }
            catch (Exception)
            {
                // Skip it rather than fail; it is not a device the user could have used anyway.
            }
            finally
            {
                device.Dispose();
            }
        }
        return devices;
    }
}
