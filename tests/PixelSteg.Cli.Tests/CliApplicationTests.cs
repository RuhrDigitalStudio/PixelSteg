using PixelSteg.Cli;

namespace PixelSteg.Cli.Tests;

public sealed class CliApplicationTests
{
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
}
