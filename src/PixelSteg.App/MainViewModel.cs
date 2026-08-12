using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using PixelSteg.Core;

namespace PixelSteg.App;

public enum OperationMode { Encode, Decode }

public sealed class MainViewModel : INotifyPropertyChanged
{
    private OperationMode _mode = OperationMode.Encode;
    private string _inputPath = string.Empty, _destinationPath = string.Empty, _statusMessage = "Choose a file to begin.", _originalFileName = string.Empty;
    private bool _isBusy;
    private int _progress;
    private CancellationTokenSource? _cancellation;
    public event PropertyChangedEventHandler? PropertyChanged;
    public OperationMode Mode { get => _mode; set { if (_mode != value) { _mode = value; OnChanged(); OnChanged(nameof(InputHint)); OnChanged(nameof(ActionLabel)); Validate(); } } }
    public string InputPath { get => _inputPath; set { _inputPath = value; OnChanged(); Validate(); } }
    public string DestinationPath { get => _destinationPath; set { _destinationPath = value; OnChanged(); Validate(); } }
    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnChanged(); } }
    public string OriginalFileName { get => _originalFileName; private set { _originalFileName = value; OnChanged(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnChanged(); OnChanged(nameof(CanStart)); } }
    public int Progress { get => _progress; private set { _progress = value; OnChanged(); } }
    public bool CanStart => !IsBusy && !string.IsNullOrWhiteSpace(InputPath) && !string.IsNullOrWhiteSpace(DestinationPath);
    public string InputHint => Mode == OperationMode.Encode ? "Choose any file to store as a PNG" : "Choose a PNG to validate and decode";
    public string ActionLabel => Mode == OperationMode.Encode ? "Encode to PNG" : "Validate and decode";
    public void SetMode(OperationMode mode) => Mode = mode;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(InputPath)) StatusMessage = "Choose an input file first.";
        else if (string.IsNullOrWhiteSpace(DestinationPath)) StatusMessage = Mode == OperationMode.Encode ? "Choose where to save the PNG." : "Choose a destination folder.";
        OnChanged(nameof(CanStart));
    }
    public void BeginOperation() { if (IsBusy) return; _cancellation = new CancellationTokenSource(); IsBusy = true; Progress = 0; }
    public void CancelOperation() { _cancellation?.Cancel(); if (IsBusy) StatusMessage = "Cancelling operation..."; }
    public bool NeedsOverwriteConfirmation(bool outputExists) => Mode == OperationMode.Decode && outputExists;
    public async Task RunAsync(bool overwriteApproved)
    {
        Validate(); if (!CanStart) return; BeginOperation();
        var cancellation = _cancellation!;
        try
        {
            var token = cancellation.Token; Progress = 15; OriginalFileName = string.Empty;
            if (Mode == OperationMode.Encode)
            {
                if (File.Exists(DestinationPath)) throw new PixelStegException("The PNG already exists. Choose another destination.");
                await using var input = new FileStream(InputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var container = new MemoryStream();
                await ContainerCodec.PackAsync(input, Path.GetFileName(InputPath), container, token);
                container.Position = 0; Progress = 50;
                await AtomicFileWriter.WriteAsync(DestinationPath, overwrite: false, (png, writeToken) => PixelCodec.EncodeAsync(container, png, writeToken), token);
                StatusMessage = "PNG created. The original content was never opened or executed.";
            }
            else
            {
                await using var png = new FileStream(InputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var container = new MemoryStream();
                await PixelCodec.DecodeAsync(png, container, token); container.Position = 0; Progress = 50;
                await using var decoded = new MemoryStream(); var header = await ContainerCodec.UnpackAsync(container, decoded, token);
                OriginalFileName = header.FileName;
                var safeName = SanitizeDecodedFileName(header.FileName);
                var root = Path.GetFullPath(DestinationPath);
                Directory.CreateDirectory(root);
                var output = Path.GetFullPath(Path.Combine(root, safeName));
                if (!output.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new PixelStegException("The decoded file name is not safe for the selected folder.");
                if (File.Exists(output) && !overwriteApproved) throw new PixelStegException("A decoded file already exists. Confirm overwrite to replace it.");
                decoded.Position = 0;
                await AtomicFileWriter.WriteAsync(output, overwriteApproved, (file, writeToken) => decoded.CopyToAsync(file, writeToken), token);
                StatusMessage = $"Integrity verified. Decoded '{safeName}' without opening or executing it.";
            }
            Progress = 100;
        }
        catch (OperationCanceledException) { StatusMessage = "Operation cancelled."; }
        catch (Exception ex) { StatusMessage = $"Unable to finish: {ex.Message}"; }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
            IsBusy = false;
        }
    }
    public static string SanitizeDecodedFileName(string embeddedName)
    {
        var name = Path.GetFileName(embeddedName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") return "decoded.bin";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
