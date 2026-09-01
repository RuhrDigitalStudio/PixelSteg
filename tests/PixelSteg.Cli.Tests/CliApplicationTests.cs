using PixelSteg.Cli;
using PixelSteg.Core;

namespace PixelSteg.Cli.Tests;

public sealed class CliApplicationTests
{
    [Theory]
    [InlineData("balanced")]
    [InlineData("dense")]
    [InlineData("adaptive")]
    [InlineData("resilient")]
    public async Task EmbedMessageThenRead_AutoDetectsEveryProfile(string profile)
    {
        var root = CreateDirectory();
        try
        {
            var cover = Path.Combine(root, "cover.png");
            var carrier = Path.Combine(root, "carrier.png");
            await WriteCoverAsync(cover, 100, 100);
            var embedError = new StringWriter();

            var embed = await CliApplication.RunAsync(
                ["embed-message", cover, carrier, "--profile", profile, "--no-compress"],
                TextWriter.Null,
                embedError,
                CancellationToken.None,
                new StringReader("Meet at 18:00."));
            var message = new StringWriter();
            var read = await CliApplication.RunAsync(
                ["read-message", carrier],
                message,
                TextWriter.Null,
                CancellationToken.None);

            Assert.Equal(0, embed);
            Assert.Equal(0, read);
            Assert.Equal("Meet at 18:00.", message.ToString());
            Assert.Contains(profile, embedError.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task EmbedFilesThenExtract_PreservesTwoEncryptedEntries()
    {
        var root = CreateDirectory();
        try
        {
            var cover = Path.Combine(root, "cover.png");
            var carrier = Path.Combine(root, "carrier.png");
            var first = Path.Combine(root, "first.txt");
            var second = Path.Combine(root, "second.bin");
            var extracted = Path.Combine(root, "extracted");
            await WriteCoverAsync(cover, 160, 160);
            await File.WriteAllTextAsync(first, "first file");
            await File.WriteAllBytesAsync(second, [0, 1, 2, 255]);
            static string? EnvironmentReader(string name) => name == "PIXELSTEG_TEST_PASSWORD" ? "test password" : null;

            var embed = await CliApplication.RunAsync(
                ["embed-file", cover, carrier, first, second, "--profile", "resilient", "--password-env", "PIXELSTEG_TEST_PASSWORD"],
                TextWriter.Null,
                TextWriter.Null,
                CancellationToken.None,
                TextReader.Null,
                EnvironmentReader);
            var inspectOutput = new StringWriter();
            var inspect = await CliApplication.RunAsync(
                ["inspect", carrier],
                inspectOutput,
                TextWriter.Null,
                CancellationToken.None);
            var extract = await CliApplication.RunAsync(
                ["extract", carrier, extracted, "--password-env", "PIXELSTEG_TEST_PASSWORD"],
                TextWriter.Null,
                TextWriter.Null,
                CancellationToken.None,
                TextReader.Null,
                EnvironmentReader);

            Assert.Equal(0, embed);
            Assert.Equal(0, inspect);
            Assert.Equal(0, extract);
            Assert.Contains("profile: resilient", inspectOutput.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("encrypted: yes", inspectOutput.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("first file", await File.ReadAllTextAsync(Path.Combine(extracted, "first.txt")));
            Assert.Equal(new byte[] { 0, 1, 2, 255 }, await File.ReadAllBytesAsync(Path.Combine(extracted, "second.bin")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RunAsync_RejectsInvalidArgumentsWithUsageExitCode()
    {
        var result = await CliApplication.RunAsync([], TextWriter.Null, TextWriter.Null, CancellationToken.None);
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_RefusesToOverwriteAnExistingPng()
    {
        var root = CreateDirectory();
        try
        {
            var input = Path.Combine(root, "safe.txt");
            var output = Path.Combine(root, "safe.png");
            await File.WriteAllTextAsync(input, "safe input");
            await File.WriteAllTextAsync(output, "keep this");
            var error = new StringWriter();
            var result = await CliApplication.RunAsync(["encode", input, output], TextWriter.Null, error, CancellationToken.None);
            Assert.Equal(3, result);
            Assert.Equal("keep this", await File.ReadAllTextAsync(output));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("..\\escape.txt", "escape.txt")]
    [InlineData("/absolute.txt", "absolute.txt")]
    [InlineData("normal.txt", "normal.txt")]
    public void SanitizeEmbeddedFileName_AlwaysReturnsOnlyALocalFileName(string embedded, string expected)
    {
        Assert.Equal(expected, FileSafety.SanitizeEmbeddedFileName(embedded));
    }

    [Fact]
    public async Task RunAsync_ReturnsCancellationExitCode()
    {
        var root = CreateDirectory();
        try
        {
            var input = Path.Combine(root, "safe.txt");
            await File.WriteAllTextAsync(input, "safe input");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var result = await CliApplication.RunAsync(["encode", input, Path.Combine(root, "safe.png")], TextWriter.Null, TextWriter.Null, cancellation.Token);
            Assert.Equal(4, result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RunAsync_ReturnsProcessingFailureForInvalidPng()
    {
        var root = CreateDirectory();
        try
        {
            var input = Path.Combine(root, "invalid.png");
            await File.WriteAllBytesAsync(input, [1, 2, 3]);
            var result = await CliApplication.RunAsync(["decode", input, root], TextWriter.Null, TextWriter.Null, CancellationToken.None);
            Assert.Equal(1, result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PixelStegTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
