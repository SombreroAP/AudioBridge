using System.IO;
using System.Text.Json;
using AudioBridge.Core;
using AudioBridge.Windows;

namespace AudioBridge.App;

/// <summary>
/// Remembers the user's choices so the second launch is a single click.
///
/// Stored per-user under AppData rather than next to the executable, because the .exe is
/// copied around and often lives in a read-only or synced folder.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public PeerRole Role { get; set; } = PeerRole.Unconfigured;
    public string? RenderDeviceId { get; set; }
    public string? CaptureDeviceId { get; set; }
    public string LatencyProfileName { get; set; } = LatencyProfile.Balanced.Name;
    public string? LastPeerAddress { get; set; }

    /// <summary>A stable identity for this install, so the other PC can tell a restart from a
    /// different machine.</summary>
    public Guid InstanceId { get; set; } = Guid.NewGuid();

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioBridge",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings must never stop the app launching; the user just
            // re-picks their devices.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to persist preferences is not worth interrupting a session for.
        }
    }

    public LatencyProfile Latency => LatencyProfile.ByName(LatencyProfileName);

    public BridgeSettings ToBridgeSettings() => new()
    {
        Role = Role,
        RenderDeviceId = RenderDeviceId,
        CaptureDeviceId = CaptureDeviceId,
        JitterDepth = Latency.JitterDepth,
        RenderLatencyMs = Latency.RenderLatencyMs,
        BlockMilliseconds = Latency.BlockMilliseconds,
        MaxPlaybackBufferMs = Latency.MaxPlaybackBufferMs,
    };
}
