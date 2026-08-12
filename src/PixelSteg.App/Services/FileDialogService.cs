using System.Windows.Forms;

namespace PixelSteg.App.Services;

public sealed class FileDialogService
{
    public string? ChooseInput(OperationMode mode)
    {
        using var dialog = new OpenFileDialog { Filter = mode == OperationMode.Encode ? "All files|*.*" : "PNG files|*.png", Title = mode == OperationMode.Encode ? "Choose a file to encode" : "Choose a PixelSteg PNG" };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
    public string? ChooseEncodeDestination()
    {
        using var dialog = new SaveFileDialog { Filter = "PNG files|*.png", AddExtension = true, DefaultExt = "png", Title = "Save encoded PNG" };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
    public string? ChooseDecodeDirectory()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose a folder for the verified decoded file", UseDescriptionForTitle = true };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
