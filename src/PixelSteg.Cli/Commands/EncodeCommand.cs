using PixelSteg.Core;

namespace PixelSteg.Cli;

public static class EncodeCommand
{
    public static async Task<int> RunAsync(string inputPath, string outputPath, TextWriter error, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("Input file was not found.", inputPath);
        if (File.Exists(outputPath)) throw new FileExistsException("Refusing to overwrite the existing PNG. Choose a new path.");
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (outputDirectory is null) throw new ArgumentException("The output path is invalid.");
        Directory.CreateDirectory(outputDirectory);
        await error.WriteLineAsync("Encoding a non-executable PNG container...");
        await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var container = new MemoryStream();
        await ContainerCodec.PackAsync(input, Path.GetFileName(inputPath), container, cancellationToken);
        container.Position = 0;
        await AtomicFileWriter.WriteAsync(outputPath, overwrite: false, (png, token) => PixelCodec.EncodeAsync(container, png, token), cancellationToken);
        await error.WriteLineAsync("Encoded safely. No content was executed.");
        return 0;
    }
}
