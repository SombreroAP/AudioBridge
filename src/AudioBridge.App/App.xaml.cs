using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace AudioBridge.App;

public partial class App : Application
{
    // Held for the life of the process so a second launch can tell the first is running.
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\AudioBridge.SingleInstance", out var first);
        if (!first)
        {
            // Two copies would fight over the audio port and the microphone. Most likely the
            // user closed the window to the tray and forgot.
            MessageBox.Show("AudioBridge is already running -- look for its icon in the system tray.",
                "AudioBridge", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Install these before anything else runs, so even a failure inside MainWindow's
        // constructor produces a visible message rather than a silent exit.
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Log.Write($"--- AudioBridge {version} starting on {Environment.OSVersion} ({Environment.MachineName}) ---");

        base.OnStartup(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write("Unhandled UI exception", e.Exception);
        Report(e.Exception);
        // Keep running where we can: a failure in one handler shouldn't close the window and
        // take the log message with it.
        e.Handled = true;
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Write("Unhandled exception", exception);
            Report(exception);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void Report(Exception exception) =>
        MessageBox.Show(
            $"{exception.Message}\n\nDetails were written to:\n{Log.FilePath}",
            "AudioBridge hit a problem",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
}
