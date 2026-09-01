namespace PixelSteg.Core;

public static class StegoCodec
{
    private const int LocatorChannels = StegoEnvelope.LocatorSize * 8;
    private const int SaltOffset = 22;

    public static IReadOnlyList<StegoCapacity> Measure(PngImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var stableChannels = EmbeddingProfiles.StableChannels(image).LongCount();
        var bodyChannels = Math.Max(0, stableChannels - LocatorChannels);
        return
        [
            new(EmbeddingProfile.Balanced, stableChannels, LocatorChannels, bodyChannels / 8),
            new(EmbeddingProfile.Dense, stableChannels, LocatorChannels, bodyChannels / 4),
            new(EmbeddingProfile.Adaptive, stableChannels, LocatorChannels, bodyChannels / 8),
            new(EmbeddingProfile.Resilient, stableChannels, LocatorChannels, bodyChannels / 12)
        ];
    }

    public static StegoEmbedResult Embed(
        PngImage cover,
        ReadOnlySpan<byte> payload,
        EmbeddingProfile profile,
        StegoProtection protection)
    {
        ArgumentNullException.ThrowIfNull(cover);
        var frame = StegoEnvelope.Protect(payload, profile, protection);
        var capacity = Measure(cover).Single(item => item.Profile == profile);
        if (capacity.StableChannels < LocatorChannels || frame.Body.LongLength > capacity.AvailablePayloadBytes)
            throw new PixelStegException(
                $"The {profile} profile needs {frame.Body.LongLength:N0} bytes but this cover provides {capacity.AvailablePayloadBytes:N0} bytes.");

        var result = cover.Clone();
        var locatorIndexes = EmbeddingProfiles.StableChannels(cover).Take(LocatorChannels).ToArray();
        WriteOneBit(frame.Locator, result.Pixels, locatorIndexes);

        var bodyIndexes = EmbeddingProfiles.BodyChannels(
            cover,
            profile,
            LocatorChannels,
            frame.Locator.AsSpan(SaltOffset, 16));
        var usedBodyChannels = profile switch
        {
            EmbeddingProfile.Dense => WriteTwoBits(frame.Body, result.Pixels, bodyIndexes),
            EmbeddingProfile.Resilient => WriteHamming(frame.Body, result.Pixels, bodyIndexes),
            _ => WriteOneBit(frame.Body, result.Pixels, bodyIndexes)
        };

        var quality = ImageQuality.Compare(cover, result, LocatorChannels + usedBodyChannels);
        return new StegoEmbedResult(result, frame.Info, quality);
    }

    public static StegoFrameInfo Inspect(PngImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var locatorIndexes = EmbeddingProfiles.StableChannels(image).Take(LocatorChannels).ToArray();
        if (locatorIndexes.Length < LocatorChannels)
            return new StegoFrameInfo(false, null, false, false, 0);
        return StegoEnvelope.Inspect(ReadOneBit(image.Pixels, locatorIndexes, StegoEnvelope.LocatorSize));
    }

    public static StegoExtractResult Extract(PngImage image, string? password)
    {
        ArgumentNullException.ThrowIfNull(image);
        var locatorIndexes = EmbeddingProfiles.StableChannels(image).Take(LocatorChannels).ToArray();
        if (locatorIndexes.Length < LocatorChannels)
            throw new PixelStegException("No PixelSteg locator was found.");
        var locator = ReadOneBit(image.Pixels, locatorIndexes, StegoEnvelope.LocatorSize);
        var info = StegoEnvelope.Inspect(locator);
        if (!info.IsPresent || info.Profile is null)
            throw new PixelStegException("No PixelSteg locator was found.");

        var bodyLength = checked((int)info.EnvelopeLength);
        var bodyIndexes = EmbeddingProfiles.BodyChannels(
            image,
            info.Profile.Value,
            LocatorChannels,
            locator.AsSpan(SaltOffset, 16));
        byte[] body;
        var corrected = 0;
        switch (info.Profile.Value)
        {
            case EmbeddingProfile.Dense:
                body = ReadTwoBits(image.Pixels, bodyIndexes, bodyLength);
                break;
            case EmbeddingProfile.Resilient:
                (body, corrected) = ReadHamming(image.Pixels, bodyIndexes, bodyLength);
                break;
            default:
                body = ReadOneBit(image.Pixels, bodyIndexes, bodyLength);
                break;
        }

        return new StegoExtractResult(
            StegoEnvelope.Unprotect(locator, body, password),
            info,
            corrected);
    }

    private static int WriteOneBit(byte[] source, byte[] pixels, IEnumerable<int> indexes)
    {
        using var iterator = indexes.GetEnumerator();
        var used = 0;
        foreach (var value in source)
        {
            for (var bit = 7; bit >= 0; bit--)
            {
                if (!iterator.MoveNext()) throw new PixelStegException("The carrier ended before the payload was written.");
                pixels[iterator.Current] = (byte)((pixels[iterator.Current] & 0xfe) | ((value >> bit) & 1));
                used++;
            }
        }
        return used;
    }

    private static byte[] ReadOneBit(byte[] pixels, IEnumerable<int> indexes, int byteCount)
    {
        using var iterator = indexes.GetEnumerator();
        var output = new byte[byteCount];
        for (var index = 0; index < output.Length; index++)
        {
            byte value = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                if (!iterator.MoveNext()) throw new PixelStegException("The carrier ended before the payload was recovered.");
                value = (byte)((value << 1) | (pixels[iterator.Current] & 1));
            }
            output[index] = value;
        }
        return output;
    }

    private static int WriteTwoBits(byte[] source, byte[] pixels, IEnumerable<int> indexes)
    {
        using var iterator = indexes.GetEnumerator();
        var used = 0;
        foreach (var value in source)
        {
            for (var shift = 6; shift >= 0; shift -= 2)
            {
                if (!iterator.MoveNext()) throw new PixelStegException("The carrier ended before the payload was written.");
                pixels[iterator.Current] = (byte)((pixels[iterator.Current] & 0xfc) | ((value >> shift) & 3));
                used++;
            }
        }
        return used;
    }

    private static byte[] ReadTwoBits(byte[] pixels, IEnumerable<int> indexes, int byteCount)
    {
        using var iterator = indexes.GetEnumerator();
        var output = new byte[byteCount];
        for (var index = 0; index < output.Length; index++)
        {
            byte value = 0;
            for (var pair = 0; pair < 4; pair++)
            {
                if (!iterator.MoveNext()) throw new PixelStegException("The carrier ended before the payload was recovered.");
                value = (byte)((value << 2) | (pixels[iterator.Current] & 3));
            }
            output[index] = value;
        }
        return output;
    }

    private static int WriteHamming(byte[] source, byte[] pixels, IEnumerable<int> indexes)
    {
        using var iterator = indexes.GetEnumerator();
        var used = 0;
        foreach (var value in source)
        {
            var encoded = HammingCodec.Encode(value);
            for (var bit = 0; bit < 12; bit++)
            {
                if (!iterator.MoveNext()) throw new PixelStegException("The carrier ended before the payload was written.");
                pixels[iterator.Current] = (byte)((pixels[iterator.Current] & 0xfe) | ((encoded >> bit) & 1));
                used++;
            }
        }
        return used;
    }

    private static (byte[] Body, int Corrected) ReadHamming(
        byte[] pixels,
        IEnumerable<int> indexes,
        int byteCount)
    {
        using var iterator = indexes.GetEnumerator();
        var output = new byte[byteCount];
        var corrected = 0;
        for (var index = 0; index < output.Length; index++)
        {
            ushort encoded = 0;
            for (var bit = 0; bit < 12; bit++)
            {
                if (!iterator.MoveNext()) throw new PixelStegException("The carrier ended before the payload was recovered.");
                encoded |= (ushort)((pixels[iterator.Current] & 1) << bit);
            }
            var decoded = HammingCodec.Decode(encoded);
            output[index] = decoded.Value;
            if (decoded.Corrected) corrected++;
        }
        return (output, corrected);
    }
}
