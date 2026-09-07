using System.IO;

namespace AudioBridge.App;

/// <summary>
/// Append-only startup and error log.
///
/// A WPF app is a GUI-subsystem binary: if it throws before the window appears, the user
/// sees literally nothing and there is no console to read. This log is how that failure
/// becomes diagnosable instead of invisible.
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AudioBridge",
        "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // Keep the file from growing without bound across many sessions.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024)
                    File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never be the thing that breaks the app.
        }
    }

    public static void Write(string context, Exception exception) =>
        Write($"{context}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception}");
}
