using System.Diagnostics;
using System.Windows;

namespace AudioBridge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    private void OpenCableSite(object sender, RoutedEventArgs e)
    {
        // UseShellExecute is required to hand a URL to the default browser.
        Process.Start(new ProcessStartInfo(_viewModel.CableInstallUri) { UseShellExecute = true });
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        await _viewModel.DisposeAsync();
    }
}
