using System.Diagnostics;
using System.Windows;

namespace AudioBridge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        Log.Write("Window initialised; building view model.");

        try
        {
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
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

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_viewModel is not null) await _viewModel.DisposeAsync();
    }
}
