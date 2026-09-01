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
