using System.Windows.Forms;

namespace PixelSteg.App.Services;

public sealed class FileDialogService
{
    public string? ChoosePng(string title)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "PNG images|*.png",
            Title = title
        };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }

    public string? ChoosePayloadFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "All files|*.*",
            Title = "Choose a file to hide"
        };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }

    public string? ChooseCarrierDestination()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG images|*.png",
            AddExtension = true,
            DefaultExt = "png",
            Title = "Save the PixelSteg carrier"
        };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }

    public string? ChooseRecoveryDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder for recovered messages and files",
            UseDescriptionForTitle = true
        };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
