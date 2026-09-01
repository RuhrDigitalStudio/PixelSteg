namespace PixelSteg.Core;

public enum EmbeddingProfile : byte
{
    Balanced = 1,
    Dense = 2,
    Adaptive = 3,
    Resilient = 4
}

public sealed record StegoProtection(bool Compress, string? Password);

public sealed record StegoFrameInfo(
    bool IsPresent,
    EmbeddingProfile? Profile,
    bool IsCompressed,
    bool IsEncrypted,
    long EnvelopeLength);

public sealed record StegoFrame(byte[] Locator, byte[] Body, StegoFrameInfo Info);

public sealed record StegoCapacity(
    EmbeddingProfile Profile,
    long StableChannels,
    long LocatorChannels,
    long AvailablePayloadBytes);

public sealed record ImageQualityReport(
    long ChangedChannels,
    int MaximumChannelDelta,
    double MeanSquaredError,
    double Psnr,
    double Ssim,
    double UsedCapacityRatio);

public sealed record StegoEmbedResult(
    PngImage Image,
    StegoFrameInfo Frame,
    ImageQualityReport Quality);

public sealed record StegoExtractResult(
    byte[] Payload,
    StegoFrameInfo Frame,
    int CorrectedCodewords);
