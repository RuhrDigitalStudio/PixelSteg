using PixelSteg.App;
using PixelSteg.Core;
using System.IO;

namespace PixelSteg.App.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void SetMode_ChangesTheVisibleWorkflow()
    {
        var viewModel = new MainViewModel();

        viewModel.SetMode(OperationMode.Reveal);

        Assert.Equal(OperationMode.Reveal, viewModel.Mode);
        Assert.True(viewModel.IsRevealMode);
        Assert.False(viewModel.IsHideMode);
        Assert.Contains("carrier", viewModel.InputHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ExplainsMissingCover()
    {
        var viewModel = new MainViewModel();

        viewModel.Validate();

        Assert.Equal("Choose a cover PNG first.", viewModel.StatusMessage);
        Assert.False(viewModel.CanStart);
    }

    [Fact]
    public async Task RefreshCoverAsync_ReportsCapacityForEveryProfile()
    {
        var root = CreateDirectory();
        try
        {
            var cover = Path.Combine(root, "cover.png");
            await WriteCoverAsync(cover, 40, 40);
            var viewModel = new MainViewModel { CoverPath = cover };

            await viewModel.RefreshCoverAsync();

            Assert.Equal(4, viewModel.Profiles.Count);
            Assert.All(viewModel.Profiles, profile => Assert.True(profile.CapacityBytes > 0));
            Assert.Contains("40 × 40", viewModel.CoverSummary);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RunAsync_HidesAndRevealsAnEncryptedMessage()
    {
        var root = CreateDirectory();
        try
        {
            var cover = Path.Combine(root, "cover.png");
            var carrier = Path.Combine(root, "carrier.png");
            var extracted = Path.Combine(root, "extracted");
            await WriteCoverAsync(cover, 100, 100);
            var hide = new MainViewModel
            {
                CoverPath = cover,
                DestinationPath = carrier,
                PayloadMode = PayloadMode.Message,
                MessageText = "Die Nachricht bleibt lokal.",
                SelectedProfile = EmbeddingProfile.Resilient,
                Password = "test password"
            };

            await hide.RunAsync(overwriteApproved: false);

            Assert.True(File.Exists(carrier));
            Assert.Contains("Resilient", hide.QualitySummary);
            Assert.Empty(hide.Password);

            var reveal = new MainViewModel
            {
                Mode = OperationMode.Reveal,
                CarrierPath = carrier,
                DestinationPath = extracted,
                Password = "test password"
            };
            await reveal.RunAsync(overwriteApproved: false);

            Assert.Equal("Die Nachricht bleibt lokal.", reveal.RecoveredMessage);
            Assert.Equal("Die Nachricht bleibt lokal.", await File.ReadAllTextAsync(Path.Combine(extracted, "message.txt")));
            Assert.Contains("Resilient", reveal.DetectedProfile);
            Assert.Empty(reveal.Password);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RunAsync_WrongPasswordLeavesDestinationEmpty()
    {
        var root = CreateDirectory();
        try
        {
            var cover = Path.Combine(root, "cover.png");
            var carrier = Path.Combine(root, "carrier.png");
            var extracted = Path.Combine(root, "extracted");
            await WriteCoverAsync(cover, 100, 100);
            var hide = new MainViewModel
            {
                CoverPath = cover,
                DestinationPath = carrier,
                MessageText = "authenticated",
                Password = "right password"
            };
            await hide.RunAsync(overwriteApproved: false);
            var reveal = new MainViewModel
            {
                Mode = OperationMode.Reveal,
                CarrierPath = carrier,
                DestinationPath = extracted,
                Password = "wrong password"
            };

            await reveal.RunAsync(overwriteApproved: false);

            Assert.False(Directory.Exists(extracted));
            Assert.Contains("authenticated", reveal.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CancelOperation_KeepsTheViewModelBusyUntilTheRunningOperationFinalizes()
    {
        var viewModel = new MainViewModel();
        viewModel.BeginOperation();

        viewModel.CancelOperation();

        Assert.True(viewModel.IsBusy);
        Assert.Equal("Cancelling operation...", viewModel.StatusMessage);
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PixelStegTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task WriteCoverAsync(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var x = pixel % width;
            var y = pixel / width;
            pixels[pixel * 4] = (byte)((x * 17 + y * 29) & 255);
            pixels[pixel * 4 + 1] = (byte)((x * 43 + y * 11) & 255);
            pixels[pixel * 4 + 2] = (byte)((x * 7 + y * 61) & 255);
            pixels[pixel * 4 + 3] = 255;
        }
        await using var output = File.Create(path);
        await PngCodec.WriteAsync(new PngImage(width, height, false, pixels), output, CancellationToken.None);
    }
}
