using PixelSteg.App.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PixelSteg.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly FileDialogService _dialogs = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void HideModeClick(object sender, RoutedEventArgs e) => _viewModel.SetMode(OperationMode.Hide);

    private void RevealModeClick(object sender, RoutedEventArgs e) => _viewModel.SetMode(OperationMode.Reveal);

    private void MessageModeClick(object sender, RoutedEventArgs e) => _viewModel.SetPayloadMode(PayloadMode.Message);

    private void FileModeClick(object sender, RoutedEventArgs e) => _viewModel.SetPayloadMode(PayloadMode.File);

    private async void BrowseCoverClick(object sender, RoutedEventArgs e)
    {
        var path = _dialogs.ChoosePng("Choose a lossless cover PNG");
        if (path is null) return;
        _viewModel.CoverPath = path;
        await _viewModel.RefreshCoverAsync();
    }

    private void BrowsePayloadClick(object sender, RoutedEventArgs e)
    {
        var path = _dialogs.ChoosePayloadFile();
        if (path is not null) _viewModel.PayloadPath = path;
    }

    private async void BrowseCarrierClick(object sender, RoutedEventArgs e)
    {
        var path = _dialogs.ChoosePng("Choose a PixelSteg carrier");
        if (path is null) return;
        _viewModel.CarrierPath = path;
        await _viewModel.InspectCarrierAsync();
    }

    private void BrowseDestinationClick(object sender, RoutedEventArgs e)
    {
        var path = _viewModel.IsHideMode
            ? _dialogs.ChooseCarrierDestination()
            : _dialogs.ChooseRecoveryDirectory();
        if (path is not null) _viewModel.DestinationPath = path;
    }

    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox) _viewModel.Password = passwordBox.Password;
    }

    private async void RunClick(object sender, RoutedEventArgs e)
    {
        var overwrite = false;
        var destinationHasContent = _viewModel.IsHideMode
            ? File.Exists(_viewModel.DestinationPath)
            : Directory.Exists(_viewModel.DestinationPath) && Directory.EnumerateFileSystemEntries(_viewModel.DestinationPath).Any();
        if (destinationHasContent)
        {
            overwrite = System.Windows.MessageBox.Show(
                "The destination already contains data. Replace files with matching names?",
                "PixelSteg",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        await _viewModel.RunAsync(overwrite);
        PasswordInput.Clear();
        RevealPasswordInput.Clear();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => _viewModel.CancelOperation();

    private async void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] { Length: 1 } paths) return;
        if (_viewModel.IsRevealMode)
        {
            _viewModel.CarrierPath = paths[0];
            await _viewModel.InspectCarrierAsync();
        }
        else if (string.IsNullOrWhiteSpace(_viewModel.CoverPath))
        {
            _viewModel.CoverPath = paths[0];
            await _viewModel.RefreshCoverAsync();
        }
        else
        {
            _viewModel.SetPayloadMode(PayloadMode.File);
            _viewModel.PayloadPath = paths[0];
        }
    }
}
