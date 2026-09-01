using PixelSteg.Core;
using System.Text;

namespace PixelSteg.Cli;

public static class ExtractCommand
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<int> RunAsync(
        string carrierPath,
        string outputDirectory,
        bool overwrite,
        string? password,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var (bundle, extracted) = await ReadBundleAsync(carrierPath, password, cancellationToken);
        var destinations = bundle.Entries
            .Select(entry => (Entry: entry, Path: FileSafety.ResolveOutputPath(outputDirectory, entry.Name)))
            .ToArray();
        if (!overwrite && destinations.Any(item => File.Exists(item.Path)))
            throw new FileExistsException("Refusing to overwrite an extracted file. Re-run with --overwrite only if intended.");

        Directory.CreateDirectory(outputDirectory);
        foreach (var item in destinations)
        {
            await AtomicFileWriter.WriteAsync(
                item.Path,
                overwrite,
                (output, token) => output.WriteAsync(item.Entry.Content, token).AsTask(),
                cancellationToken);
        }

        await error.WriteLineAsync(
            $"Extracted {destinations.Length} item(s) after integrity validation. " +
            $"Corrected codewords: {extracted.CorrectedCodewords}.");
        return 0;
    }

    public static async Task<int> ReadMessagesAsync(
        string carrierPath,
        string? password,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var (bundle, _) = await ReadBundleAsync(carrierPath, password, cancellationToken);
        var messages = bundle.Entries.Where(entry => entry.Kind == PayloadKind.Message).ToArray();
        if (messages.Length == 0) throw new PixelStegException("The carrier contains no message entry.");
        for (var index = 0; index < messages.Length; index++)
        {
            if (index > 0) await output.WriteLineAsync();
            await output.WriteAsync(StrictUtf8.GetString(messages[index].Content));
        }
        return 0;
    }

    private static async Task<(PayloadBundle Bundle, StegoExtractResult Extracted)> ReadBundleAsync(
        string carrierPath,
        string? password,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(carrierPath)) throw new FileNotFoundException("Carrier PNG was not found.", carrierPath);
        await using var carrierStream = new FileStream(carrierPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var image = await PngCodec.ReadAsync(carrierStream, cancellationToken);
        var extracted = StegoCodec.Extract(image, password);
        return (PayloadBundleCodec.Unpack(extracted.Payload), extracted);
    }
}
