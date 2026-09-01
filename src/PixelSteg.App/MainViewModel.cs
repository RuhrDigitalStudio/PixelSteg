using PixelSteg.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace PixelSteg.App;

public enum OperationMode
{
    Hide,
    Reveal
}

public enum PayloadMode
{
    Message,
    File
}

public sealed class ProfileOption
{
    public ProfileOption(EmbeddingProfile profile, string description, long capacityBytes)
    {
        Profile = profile;
        Description = description;
        CapacityBytes = capacityBytes;
    }

    public EmbeddingProfile Profile { get; }

    public string DisplayName => Profile.ToString();

    public string Description { get; }

    public long CapacityBytes { get; }

    public string CapacityLabel => CapacityBytes <= 0 ? "No capacity" : $"{CapacityBytes:N0} bytes";
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private OperationMode _mode = OperationMode.Hide;
    private PayloadMode _payloadMode = PayloadMode.Message;
    private string _coverPath = string.Empty;
    private string _payloadPath = string.Empty;
    private string _carrierPath = string.Empty;
    private string _destinationPath = string.Empty;
    private string _messageText = string.Empty;
    private string _password = string.Empty;
    private string _statusMessage = "Choose a cover PNG first.";
    private string _coverSummary = "No cover inspected";
    private string _qualitySummary = string.Empty;
    private string _detectedProfile = "Not inspected";
    private string _recoveredMessage = string.Empty;
    private bool _compress = true;
    private bool _isBusy;
    private int _progress;
    private EmbeddingProfile _selectedProfile = EmbeddingProfile.Adaptive;
    private CancellationTokenSource? _cancellation;

    public MainViewModel()
    {
        SetProfiles([]);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProfileOption> Profiles { get; } = [];

    public OperationMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            OnChanged();
            OnChanged(nameof(IsHideMode));
            OnChanged(nameof(IsRevealMode));
            OnChanged(nameof(InputHint));
            OnChanged(nameof(ActionLabel));
            Validate();
        }
    }

    public PayloadMode PayloadMode
    {
        get => _payloadMode;
        set
        {
            if (_payloadMode == value) return;
            _payloadMode = value;
            OnChanged();
            OnChanged(nameof(IsMessagePayload));
            OnChanged(nameof(IsFilePayload));
            Validate();
        }
    }

    public string CoverPath
    {
        get => _coverPath;
        set { _coverPath = value; OnChanged(); Validate(); }
    }

    public string PayloadPath
    {
        get => _payloadPath;
        set { _payloadPath = value; OnChanged(); Validate(); }
    }

    public string CarrierPath
    {
        get => _carrierPath;
        set { _carrierPath = value; OnChanged(); Validate(); }
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set { _destinationPath = value; OnChanged(); Validate(); }
    }

    public string MessageText
    {
        get => _messageText;
        set { _messageText = value; OnChanged(); Validate(); }
    }

    public string Password
    {
        get => _password;
        set { _password = value; OnChanged(); }
    }

    public bool Compress
    {
        get => _compress;
        set { _compress = value; OnChanged(); }
    }

    public EmbeddingProfile SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnChanged(); }
    }

    public string CoverSummary
    {
        get => _coverSummary;
        private set { _coverSummary = value; OnChanged(); }
    }

    public string QualitySummary
    {
        get => _qualitySummary;
        private set { _qualitySummary = value; OnChanged(); }
    }

    public string DetectedProfile
    {
        get => _detectedProfile;
        private set { _detectedProfile = value; OnChanged(); }
    }

    public string RecoveredMessage
    {
        get => _recoveredMessage;
        private set { _recoveredMessage = value; OnChanged(); OnChanged(nameof(HasRecoveredMessage)); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; OnChanged(); OnChanged(nameof(CanStart)); }
    }

    public int Progress
    {
        get => _progress;
        private set { _progress = value; OnChanged(); }
    }

    public bool IsHideMode => Mode == OperationMode.Hide;

    public bool IsRevealMode => Mode == OperationMode.Reveal;

    public bool IsMessagePayload => PayloadMode == PayloadMode.Message;

    public bool IsFilePayload => PayloadMode == PayloadMode.File;

    public bool HasRecoveredMessage => !string.IsNullOrEmpty(RecoveredMessage);

    public string InputHint => IsHideMode
        ? "Choose a cover, then hide a message or file"
        : "Choose a PixelSteg carrier and recover its contents";

    public string ActionLabel => IsHideMode ? "Create carrier" : "Verify and recover";

    public bool CanStart => !IsBusy && (IsHideMode
        ? File.Exists(CoverPath) && !string.IsNullOrWhiteSpace(DestinationPath) &&
          (IsMessagePayload ? !string.IsNullOrEmpty(MessageText) : File.Exists(PayloadPath))
        : File.Exists(CarrierPath) && !string.IsNullOrWhiteSpace(DestinationPath));

    public void SetMode(OperationMode mode) => Mode = mode;

    public void SetPayloadMode(PayloadMode mode) => PayloadMode = mode;

    public void Validate()
    {
        if (IsHideMode)
        {
            if (string.IsNullOrWhiteSpace(CoverPath)) StatusMessage = "Choose a cover PNG first.";
            else if (!File.Exists(CoverPath)) StatusMessage = "The selected cover PNG does not exist.";
            else if (IsMessagePayload && string.IsNullOrEmpty(MessageText)) StatusMessage = "Write a message to hide.";
            else if (IsFilePayload && !File.Exists(PayloadPath)) StatusMessage = "Choose a file to hide.";
            else if (string.IsNullOrWhiteSpace(DestinationPath)) StatusMessage = "Choose where to save the carrier PNG.";
            else StatusMessage = "Ready to create the carrier.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(CarrierPath)) StatusMessage = "Choose a carrier PNG first.";
            else if (!File.Exists(CarrierPath)) StatusMessage = "The selected carrier PNG does not exist.";
            else if (string.IsNullOrWhiteSpace(DestinationPath)) StatusMessage = "Choose a recovery folder.";
            else StatusMessage = "Ready to verify and recover the carrier.";
        }
        OnChanged(nameof(CanStart));
    }

    public async Task RefreshCoverAsync()
    {
        if (!File.Exists(CoverPath))
        {
            CoverSummary = "No valid cover selected";
            SetProfiles([]);
            Validate();
            return;
        }

        try
        {
            await using var stream = new FileStream(CoverPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var cover = await PngCodec.ReadAsync(stream, CancellationToken.None);
            SetProfiles(StegoCodec.Measure(cover));
            CoverSummary = $"{cover.Width} × {cover.Height} · {(cover.HasAlpha ? "RGBA" : "RGB")} · {Profiles[0].CapacityBytes:N0}–{Profiles[1].CapacityBytes:N0} bytes";
            StatusMessage = "Cover inspected. Choose content and a profile.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PixelStegException)
        {
            CoverSummary = "Cover could not be inspected";
            StatusMessage = ex.Message;
            SetProfiles([]);
        }
    }

    public async Task InspectCarrierAsync()
    {
        if (!File.Exists(CarrierPath))
        {
            DetectedProfile = "No carrier selected";
            return;
        }
        try
        {
            await using var stream = new FileStream(CarrierPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var image = await PngCodec.ReadAsync(stream, CancellationToken.None);
            var info = StegoCodec.Inspect(image);
            DetectedProfile = info.IsPresent
                ? $"{info.Profile} · {(info.IsEncrypted ? "encrypted" : "not encrypted")} · {info.EnvelopeLength:N0} bytes"
                : "No PixelSteg locator found";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PixelStegException)
        {
            DetectedProfile = ex.Message;
        }
    }

    public void BeginOperation()
    {
        if (IsBusy) return;
        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
    }

    public void CancelOperation()
    {
        _cancellation?.Cancel();
        if (IsBusy) StatusMessage = "Cancelling operation...";
    }

    public async Task RunAsync(bool overwriteApproved)
    {
        Validate();
        if (!CanStart) return;
        BeginOperation();
        var cancellation = _cancellation!;
        var password = Password;
        try
        {
            if (IsHideMode)
                await HideAsync(password, overwriteApproved, cancellation.Token);
            else
                await RevealAsync(password, overwriteApproved, cancellation.Token);
            Progress = 100;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PixelStegException or ArgumentException)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            Password = string.Empty;
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
            IsBusy = false;
        }
    }

    private async Task HideAsync(string password, bool overwrite, CancellationToken cancellationToken)
    {
        if (File.Exists(DestinationPath) && !overwrite)
            throw new PixelStegException("The carrier PNG already exists. Confirm replacement or choose another path.");
        Progress = 10;
        await using var coverStream = new FileStream(CoverPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var cover = await PngCodec.ReadAsync(coverStream, cancellationToken);
        SetProfiles(StegoCodec.Measure(cover));
        Progress = 30;

        PayloadEntry entry;
        if (IsMessagePayload)
        {
            entry = new PayloadEntry(
                PayloadKind.Message,
                "message.txt",
                "text/plain; charset=utf-8",
                Encoding.UTF8.GetBytes(MessageText));
        }
        else
        {
            var info = new FileInfo(PayloadPath);
            entry = new PayloadEntry(
                PayloadKind.File,
                info.Name,
                "application/octet-stream",
                await File.ReadAllBytesAsync(PayloadPath, cancellationToken));
        }

        var payload = PayloadBundleCodec.Pack(new PayloadBundle([entry]));
        var embedded = StegoCodec.Embed(
            cover,
            payload,
            SelectedProfile,
            new StegoProtection(Compress, string.IsNullOrEmpty(password) ? null : password));
        Progress = 75;
        await AtomicFileWriter.WriteAsync(
            DestinationPath,
            overwrite,
            (output, token) => PngCodec.WriteAsync(embedded.Image, output, token),
            cancellationToken);

        QualitySummary = string.Create(
            CultureInfo.InvariantCulture,
            $"{SelectedProfile} · PSNR {embedded.Quality.Psnr:0.00} dB · SSIM {embedded.Quality.Ssim:0.000000} · Δ max {embedded.Quality.MaximumChannelDelta}");
        StatusMessage = "Carrier created. The source file was read, not opened or executed.";
    }

    private async Task RevealAsync(string password, bool overwrite, CancellationToken cancellationToken)
    {
        Progress = 15;
        await using var carrierStream = new FileStream(CarrierPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var image = await PngCodec.ReadAsync(carrierStream, cancellationToken);
        var info = StegoCodec.Inspect(image);
        if (!info.IsPresent) throw new PixelStegException("No PixelSteg locator was found.");
        DetectedProfile = $"{info.Profile} · {(info.IsEncrypted ? "encrypted" : "not encrypted")} · {info.EnvelopeLength:N0} bytes";
        Progress = 35;
        var extracted = StegoCodec.Extract(image, string.IsNullOrEmpty(password) ? null : password);
        var bundle = PayloadBundleCodec.Unpack(extracted.Payload);
        Progress = 60;

        var destinations = bundle.Entries
            .Select(entry => (Entry: entry, Path: ResolveOutputPath(DestinationPath, entry.Name)))
            .ToArray();
        if (!overwrite && destinations.Any(item => File.Exists(item.Path)))
            throw new PixelStegException("A recovered file already exists. Confirm replacement to continue.");

        Directory.CreateDirectory(DestinationPath);
        foreach (var item in destinations)
        {
            await AtomicFileWriter.WriteAsync(
                item.Path,
                overwrite,
                (output, token) => output.WriteAsync(item.Entry.Content, token).AsTask(),
                cancellationToken);
        }

        var message = bundle.Entries.FirstOrDefault(entry => entry.Kind == PayloadKind.Message);
        RecoveredMessage = message is null ? string.Empty : new UTF8Encoding(false, true).GetString(message.Content);
        QualitySummary = $"{bundle.Entries.Count} item(s) · {extracted.CorrectedCodewords} corrected codeword(s)";
        StatusMessage = "Carrier authenticated and recovered successfully.";
    }

    private void SetProfiles(IReadOnlyList<StegoCapacity> capacities)
    {
        var byProfile = capacities.ToDictionary(item => item.Profile, item => item.AvailablePayloadBytes);
        Profiles.Clear();
        Profiles.Add(new ProfileOption(EmbeddingProfile.Balanced, "Maximum fidelity · 1 bit/channel", byProfile.GetValueOrDefault(EmbeddingProfile.Balanced)));
        Profiles.Add(new ProfileOption(EmbeddingProfile.Dense, "Maximum capacity · 2 bits/channel", byProfile.GetValueOrDefault(EmbeddingProfile.Dense)));
        Profiles.Add(new ProfileOption(EmbeddingProfile.Adaptive, "Textured regions · low visual impact", byProfile.GetValueOrDefault(EmbeddingProfile.Adaptive)));
        Profiles.Add(new ProfileOption(EmbeddingProfile.Resilient, "Adaptive map · single-bit correction", byProfile.GetValueOrDefault(EmbeddingProfile.Resilient)));
        OnChanged(nameof(Profiles));
    }

    private static string ResolveOutputPath(string directory, string name)
    {
        var root = Path.GetFullPath(directory);
        var candidate = Path.GetFullPath(Path.Combine(root, Path.GetFileName(name)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new PixelStegException("A recovered filename is not safe for the selected folder.");
        return candidate;
    }

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
