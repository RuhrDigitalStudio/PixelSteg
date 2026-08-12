using PixelSteg.App;
using System.IO;

namespace PixelSteg.App.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void SetMode_ChangesTheVisibleWorkflow()
    {
        var viewModel = new MainViewModel();
        viewModel.SetMode(OperationMode.Decode);
        Assert.Equal(OperationMode.Decode, viewModel.Mode);
        Assert.Equal("Choose a PNG to validate and decode", viewModel.InputHint);
    }

    [Fact]
    public void Validate_ExplainsMissingInput()
    {
        var viewModel = new MainViewModel();
        viewModel.Validate();
        Assert.Equal("Choose an input file first.", viewModel.StatusMessage);
        Assert.False(viewModel.CanStart);
    }

    [Fact]
    public void CancelOperation_KeepsTheViewModelBusyUntilTheRunningOperationFinalizes()
    {
        var viewModel = new MainViewModel();
        viewModel.BeginOperation();
        Assert.True(viewModel.IsBusy);
        viewModel.CancelOperation();
        Assert.True(viewModel.IsBusy);
        Assert.Equal("Cancelling operation...", viewModel.StatusMessage);
    }

    [Fact]
    public void DecodeExistingDestination_RequestsExplicitOverwriteConfirmation()
    {
        var viewModel = new MainViewModel { Mode = OperationMode.Decode };
        Assert.True(viewModel.NeedsOverwriteConfirmation(true));
        Assert.False(viewModel.NeedsOverwriteConfirmation(false));
    }

    [Theory]
    [InlineData("..", "decoded.bin")]
    [InlineData("..\\outside.txt", "outside.txt")]
    public void SanitizeDecodedFileName_ContainsEmbeddedPaths(string embeddedName, string expected)
    {
        Assert.Equal(expected, MainViewModel.SanitizeDecodedFileName(embeddedName));
    }

    [Fact]
    public async Task RunAsync_DecodesToAContainedPathAndExposesOriginalFileName()
    {
        var root = CreateDirectory();
        var sourceDirectory = Path.Combine(root, "source");
        var destinationDirectory = Path.Combine(root, "decoded");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);
        var input = Path.Combine(sourceDirectory, "fixture.txt");
        var png = Path.Combine(root, "fixture.png");
        await File.WriteAllTextAsync(input, "inert fixture");
        try
        {
            var encode = new MainViewModel { InputPath = input, DestinationPath = png };
            await encode.RunAsync(overwriteApproved: false);
            Assert.True(File.Exists(png));

            var decode = new MainViewModel { Mode = OperationMode.Decode, InputPath = png, DestinationPath = destinationDirectory };
            await decode.RunAsync(overwriteApproved: false);
            Assert.Equal("fixture.txt", decode.OriginalFileName);
            Assert.Contains("Integrity verified", decode.StatusMessage);
            Assert.Equal("inert fixture", await File.ReadAllTextAsync(Path.Combine(destinationDirectory, "fixture.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PixelStegTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
