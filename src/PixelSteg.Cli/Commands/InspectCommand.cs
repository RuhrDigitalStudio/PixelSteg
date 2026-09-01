using PixelSteg.Core;

namespace PixelSteg.Cli;

public static class InspectCommand
{
    public static async Task<int> RunAsync(
        string carrierPath,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(carrierPath)) throw new FileNotFoundException("PNG was not found.", carrierPath);
        await using var stream = new FileStream(carrierPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var image = await PngCodec.ReadAsync(stream, cancellationToken);
        var info = StegoCodec.Inspect(image);
        await output.WriteLineAsync($"image: {image.Width}x{image.Height} {(image.HasAlpha ? "RGBA" : "RGB")}");
        foreach (var capacity in StegoCodec.Measure(image))
            await output.WriteLineAsync($"capacity {capacity.Profile.ToString().ToLowerInvariant()}: {capacity.AvailablePayloadBytes} bytes");
        if (!info.IsPresent)
        {
            await output.WriteLineAsync("profile: none");
            return 0;
        }

        await output.WriteLineAsync($"profile: {info.Profile!.Value.ToString().ToLowerInvariant()}");
        await output.WriteLineAsync($"payload bytes: {info.EnvelopeLength}");
        await output.WriteLineAsync($"compressed: {(info.IsCompressed ? "yes" : "no")}");
        await output.WriteLineAsync($"encrypted: {(info.IsEncrypted ? "yes" : "no")}");
        await output.WriteLineAsync($"error correction: {(info.Profile == EmbeddingProfile.Resilient ? "hamming(12,8)" : "none")}");
        return 0;
    }
}
