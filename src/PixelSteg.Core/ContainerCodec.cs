using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PixelSteg.Core;

public static class ContainerCodec
{
    private static readonly byte[] Magic = "PSTG"u8.ToArray();
    private const uint Version = 1;

    public static async Task PackAsync(Stream input, string fileName, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        var nameBytes = Encoding.UTF8.GetBytes(fileName ?? throw new ArgumentNullException(nameof(fileName)));
        if (nameBytes.Length == 0 || nameBytes.Length > PixelStegLimits.MaximumFileNameBytes)
            throw new PixelStegException("The file name must be between 1 and 1024 UTF-8 bytes.");

        await using var payload = new MemoryStream();
        await CopyToBoundedAsync(input, payload, PixelStegLimits.MaximumPayloadBytes, cancellationToken);

        var digest = SHA256.HashData(payload.GetBuffer().AsSpan(0, checked((int)payload.Length)));
        var prefix = new byte[4 + 4 + 2 + nameBytes.Length + 8 + digest.Length];
        Magic.CopyTo(prefix, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(8), checked((ushort)nameBytes.Length));
        nameBytes.CopyTo(prefix, 10);
        BinaryPrimitives.WriteInt64LittleEndian(prefix.AsSpan(10 + nameBytes.Length), payload.Length);
        digest.CopyTo(prefix, 18 + nameBytes.Length);
        await output.WriteAsync(prefix, cancellationToken);
        payload.Position = 0;
        await payload.CopyToAsync(output, cancellationToken);
    }

    public static async Task<ContainerHeader> UnpackAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        var fixedHeader = await ReadExactlyAsync(input, 10, cancellationToken);
        if (!fixedHeader.AsSpan(0, 4).SequenceEqual(Magic)) throw new PixelStegException("This is not a PixelSteg container.");
        var version = BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader.AsSpan(4));
        if (version != Version) throw new PixelStegException("This PixelSteg container version is not supported.");
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(8));
        if (nameLength == 0 || nameLength > PixelStegLimits.MaximumFileNameBytes) throw new PixelStegException("The embedded file name is invalid.");
        var nameBytes = await ReadExactlyAsync(input, nameLength, cancellationToken);
        string name;
        try { name = new UTF8Encoding(false, true).GetString(nameBytes); }
        catch (DecoderFallbackException) { throw new PixelStegException("The embedded file name is not valid UTF-8."); }
        var lengthAndDigest = await ReadExactlyAsync(input, 40, cancellationToken);
        var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(lengthAndDigest.AsSpan(0, 8));
        if (payloadLength < 0 || payloadLength > PixelStegLimits.MaximumPayloadBytes) throw new PixelStegException("The embedded payload length is invalid.");
        var expectedDigest = lengthAndDigest[8..];
        var payload = await ReadExactlyAsync(input, checked((int)payloadLength), cancellationToken);
        if (await input.ReadAsync(new byte[1], cancellationToken) != 0) throw new PixelStegException("The container contains trailing bytes.");
        var actualDigest = SHA256.HashData(payload);
        if (!CryptographicOperations.FixedTimeEquals(expectedDigest, actualDigest)) throw new PixelStegException("Integrity validation failed: SHA-256 digest does not match.");
        await output.WriteAsync(payload, cancellationToken);
        return new ContainerHeader(version, name, payloadLength, expectedDigest);
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream input, int length, CancellationToken cancellationToken)
    {
        var result = new byte[length];
        var offset = 0;
        while (offset < result.Length)
        {
            var read = await input.ReadAsync(result.AsMemory(offset), cancellationToken);
            if (read == 0) throw new PixelStegException("The container is truncated.");
            offset += read;
        }
        return result;
    }

    private static async Task CopyToBoundedAsync(Stream input, Stream output, long maximumLength, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) return;
            if (output.Length > maximumLength - read) throw new PixelStegException("The payload exceeds the 128 MiB safety limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
