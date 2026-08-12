using PixelSteg.Core;

namespace PixelSteg.Cli;

public static class DecodeCommand
{
    public static async Task<int> RunAsync(string inputPath, string outputDirectory, bool overwrite, TextWriter error, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("Input PNG was not found.", inputPath);
        Directory.CreateDirectory(outputDirectory);
        await error.WriteLineAsync("Validating PNG and SHA-256 integrity...");
        await using var png = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var container = new MemoryStream();
        await PixelCodec.DecodeAsync(png, container, cancellationToken);
        container.Position = 0;
        await using var decoded = new MemoryStream();
        var header = await ContainerCodec.UnpackAsync(container, decoded, cancellationToken);
        var outputPath = FileSafety.ResolveOutputPath(outputDirectory, header.FileName);
        if (File.Exists(outputPath) && !overwrite) throw new FileExistsException("Refusing to overwrite the decoded file. Re-run with --overwrite only if intended.");
        decoded.Position = 0;
        await AtomicFileWriter.WriteAsync(outputPath, overwrite, (output, token) => decoded.CopyToAsync(output, token), cancellationToken);
        await error.WriteLineAsync($"Decoded '{Path.GetFileName(outputPath)}' after integrity validation. Content was not opened or executed.");
        return 0;
    }
}
