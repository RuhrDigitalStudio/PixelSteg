using PixelSteg.Core;

namespace PixelSteg.Cli;

public static class EmbedCommand
{
    public static async Task<int> RunFilesAsync(
        string coverPath,
        string outputPath,
        IReadOnlyList<string> inputPaths,
        EmbeddingProfile profile,
        bool compress,
        string? password,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (inputPaths.Count == 0) throw new ArgumentException("Choose at least one file to embed.");
        if (inputPaths.Count > PixelStegLimits.MaximumBundleEntries)
            throw new ArgumentException($"A bundle can contain at most {PixelStegLimits.MaximumBundleEntries} entries.");
        var files = new List<(FileInfo Info, string MediaType)>(inputPaths.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long estimatedBundleSize = 8;
        foreach (var path in inputPaths)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Payload file was not found.", path);
            var info = new FileInfo(path);
            if (info.Length > PixelStegLimits.MaximumPayloadBytes)
                throw new PixelStegException($"'{info.Name}' exceeds the payload size limit.");
            if (!names.Add(info.Name))
                throw new PixelStegException("Payload file names must be unique.");
            var mediaType = MediaTypeFor(info.Extension);
            var entrySize = 45L + System.Text.Encoding.UTF8.GetByteCount(info.Name) +
                System.Text.Encoding.UTF8.GetByteCount(mediaType) + info.Length;
            if (estimatedBundleSize > PixelStegLimits.MaximumBundleBytes - entrySize)
                throw new PixelStegException("The combined payload files exceed the bundle size limit.");
            estimatedBundleSize += entrySize;
            files.Add((info, mediaType));
        }

        var entries = new List<PayloadEntry>(files.Count);
        foreach (var file in files)
        {
            entries.Add(new PayloadEntry(
                PayloadKind.File,
                file.Info.Name,
                file.MediaType,
                await File.ReadAllBytesAsync(file.Info.FullName, cancellationToken)));
        }

        return await WriteCarrierAsync(
            coverPath,
            outputPath,
            new PayloadBundle(entries),
            profile,
            compress,
            password,
            error,
            cancellationToken);
    }

    public static Task<int> RunMessageAsync(
        string coverPath,
        string outputPath,
        string message,
        EmbeddingProfile profile,
        bool compress,
        string? password,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(message)) throw new ArgumentException("The message is empty.");
        return WriteCarrierAsync(
            coverPath,
            outputPath,
            new PayloadBundle(
                [new PayloadEntry(PayloadKind.Message, "message.txt", "text/plain; charset=utf-8", System.Text.Encoding.UTF8.GetBytes(message))]),
            profile,
            compress,
            password,
            error,
            cancellationToken);
    }

    private static async Task<int> WriteCarrierAsync(
        string coverPath,
        string outputPath,
        PayloadBundle bundle,
        EmbeddingProfile profile,
        bool compress,
        string? password,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(coverPath)) throw new FileNotFoundException("Cover PNG was not found.", coverPath);
        if (File.Exists(outputPath)) throw new FileExistsException("Refusing to overwrite the existing carrier PNG.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (directory is null) throw new ArgumentException("The output path is invalid.");
        Directory.CreateDirectory(directory);

        await using var coverStream = new FileStream(coverPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var cover = await PngCodec.ReadAsync(coverStream, cancellationToken);
        var payload = PayloadBundleCodec.Pack(bundle);
        var embedded = StegoCodec.Embed(cover, payload, profile, new StegoProtection(compress, password));
        await AtomicFileWriter.WriteAsync(
            outputPath,
            overwrite: false,
            (output, token) => PngCodec.WriteAsync(embedded.Image, output, token),
            cancellationToken);

        var psnr = double.IsPositiveInfinity(embedded.Quality.Psnr)
            ? "∞"
            : embedded.Quality.Psnr.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        await error.WriteLineAsync(
            $"Embedded {bundle.Entries.Count} item(s) with the {profile.ToString().ToLowerInvariant()} profile. " +
            $"PSNR {psnr} dB, SSIM {embedded.Quality.Ssim:0.000000}, max channel change {embedded.Quality.MaximumChannelDelta}.");
        return 0;
    }

    private static string MediaTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".txt" => "text/plain",
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        _ => "application/octet-stream"
    };
}
