using System.Windows;
using PixelSteg.App.Services;

namespace PixelSteg.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly FileDialogService _dialogs = new();
    public MainWindow() { InitializeComponent(); DataContext = _viewModel; }
    private void EncodeModeClick(object sender, RoutedEventArgs e) => _viewModel.SetMode(OperationMode.Encode);
    private void DecodeModeClick(object sender, RoutedEventArgs e) => _viewModel.SetMode(OperationMode.Decode);
    private void BrowseInputClick(object sender, RoutedEventArgs e) { var value = _dialogs.ChooseInput(_viewModel.Mode); if (value is not null) _viewModel.InputPath = value; }
    private void BrowseDestinationClick(object sender, RoutedEventArgs e) { var value = _viewModel.Mode == OperationMode.Encode ? _dialogs.ChooseEncodeDestination() : _dialogs.ChooseDecodeDirectory(); if (value is not null) _viewModel.DestinationPath = value; }
    private async void RunClick(object sender, RoutedEventArgs e) { var overwrite = false; if (_viewModel.Mode == OperationMode.Decode && !string.IsNullOrWhiteSpace(_viewModel.DestinationPath) && System.Windows.MessageBox.Show("Decode only after integrity validation. Replace an existing decoded file if one has the same name?", "PixelSteg", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) overwrite = true; await _viewModel.RunAsync(overwrite); }
    private void CancelClick(object sender, RoutedEventArgs e) => _viewModel.CancelOperation();
    private void OnDrop(object sender, System.Windows.DragEventArgs e) { if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths && paths.Length == 1) _viewModel.InputPath = paths[0]; }
}
