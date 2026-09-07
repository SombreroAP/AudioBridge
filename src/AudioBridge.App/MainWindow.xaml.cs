using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace AudioBridge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel? _viewModel;
    private readonly TrayIcon _tray;
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();
        Log.Write("Window initialised; building view model.");

        _tray = new TrayIcon(show: ShowFromTray, exit: ExitApp);

        try
        {
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.Status)) _tray.SetStatus("AudioBridge — " + _viewModel.Status);
            };
            Log.Write("View model ready.");
        }
        catch (Exception ex)
        {
            // Enumerating audio devices goes through COM and can fail on an unusual audio
            // setup. Show the window with the error rather than dying before it appears.
            Log.Write("Failed to build view model", ex);
            MessageBox.Show(
                $"AudioBridge could not start up properly.\n\n{ex.Message}\n\nDetails: {Log.FilePath}",
                "AudioBridge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>In preview mode, drop the list open so a screenshot captures the popup
    /// itself rather than just the closed control.</summary>
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (Environment.GetEnvironmentVariable("AUDIOBRIDGE_DEMO") == "1")
            PeerCombo.IsDropDownOpen = true;
    }

    private void OpenCableSite(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        // UseShellExecute is required to hand a URL to the default browser.
        Process.Start(new ProcessStartInfo(_viewModel.CableInstallUri) { UseShellExecute = true });
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>The window's close button hides to the tray. Audio keeps flowing: the whole
    /// point is to leave this running for a gaming session without a window in the way.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyExit)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowHiddenHint();
            return;
        }
        base.OnClosing(e);
    }

    /// <summary>Exit from the tray menu: the only way the app actually quits.</summary>
    private void ExitApp()
    {
        _reallyExit = true;
        Close();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Log.Write("Exiting.");
        _tray.Dispose();

        // The audio device threads are foreground threads, so if any of them fails to
        // stop the process would outlive its window. Give shutdown a fair chance, then
        // end the process regardless -- a lingering invisible AudioBridge holding the
        // microphone open is far worse than a slightly abrupt exit.
        var watchdog = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => Environment.Exit(0));

        if (_viewModel is not null)
        {
            try { await _viewModel.DisposeAsync(); }
            catch (Exception ex) { Log.Write("Error during shutdown", ex); }
        }

        Application.Current.Shutdown();
        GC.KeepAlive(watchdog);
    }
}
