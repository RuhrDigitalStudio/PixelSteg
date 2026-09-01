using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PixelSteg.Core;

public static class PayloadBundleCodec
{
    private static readonly byte[] Magic = "PBND"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const ushort Version = 1;
    private const int MaximumNameBytes = 1024;
    private const int MaximumMediaTypeBytes = 256;

    public static byte[] Pack(PayloadBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.Entries.Count is < 1 or > PixelStegLimits.MaximumBundleEntries)
            throw new PixelStegException("A bundle must contain between 1 and 64 entries.");

        var prepared = new List<PreparedEntry>(bundle.Entries.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalLength = 8;
        foreach (var entry in bundle.Entries)
        {
            ValidateEntry(entry, names);
            var name = StrictUtf8.GetBytes(entry.Name);
            var mediaType = StrictUtf8.GetBytes(entry.MediaType);
            if (name.Length is 0 or > MaximumNameBytes)
                throw new PixelStegException("A bundle entry name is too long.");
            if (mediaType.Length is 0 or > MaximumMediaTypeBytes)
                throw new PixelStegException("A bundle media type is invalid.");
            if (entry.Content.LongLength > PixelStegLimits.MaximumPayloadBytes)
                throw new PixelStegException("A bundle entry exceeds the payload size limit.");

            totalLength = checked(totalLength + 45L + name.Length + mediaType.Length + entry.Content.Length);
            if (totalLength > PixelStegLimits.MaximumBundleBytes)
                throw new PixelStegException("The bundle exceeds the payload size limit.");
            prepared.Add(new PreparedEntry(entry, name, mediaType, SHA256.HashData(entry.Content)));
        }

        var output = new byte[checked((int)totalLength)];
        Magic.CopyTo(output, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(4, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(6, 2), checked((ushort)prepared.Count));
        var offset = 8;
        foreach (var item in prepared)
        {
            output[offset++] = (byte)item.Entry.Kind;
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset, 2), checked((ushort)item.Name.Length));
            offset += 2;
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset, 2), checked((ushort)item.MediaType.Length));
            offset += 2;
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(offset, 8), checked((ulong)item.Entry.Content.LongLength));
            offset += 8;
            item.Digest.CopyTo(output, offset);
            offset += item.Digest.Length;
            item.Name.CopyTo(output, offset);
            offset += item.Name.Length;
            item.MediaType.CopyTo(output, offset);
            offset += item.MediaType.Length;
            item.Entry.Content.CopyTo(output, offset);
            offset += item.Entry.Content.Length;
        }

        return output;
    }

    public static PayloadBundle Unpack(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length > PixelStegLimits.MaximumBundleBytes)
            throw new PixelStegException("The bundle exceeds the payload size limit.");
        if (encoded.Length < 8 || !encoded[..4].SequenceEqual(Magic))
            throw new PixelStegException("This is not a PixelSteg payload bundle.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(encoded.Slice(4, 2)) != Version)
            throw new PixelStegException("This payload bundle version is not supported.");

        var count = BinaryPrimitives.ReadUInt16LittleEndian(encoded.Slice(6, 2));
        if (count is < 1 or > PixelStegLimits.MaximumBundleEntries)
            throw new PixelStegException("The payload bundle entry count is invalid.");

        var entries = new List<PayloadEntry>(count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offset = 8;
        for (var index = 0; index < count; index++)
        {
            EnsureAvailable(encoded, offset, 45);
            var kind = (PayloadKind)encoded[offset++];
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(encoded.Slice(offset, 2));
            offset += 2;
            var mediaTypeLength = BinaryPrimitives.ReadUInt16LittleEndian(encoded.Slice(offset, 2));
            offset += 2;
            var contentLength = BinaryPrimitives.ReadUInt64LittleEndian(encoded.Slice(offset, 8));
            offset += 8;
            var digest = encoded.Slice(offset, 32).ToArray();
            offset += digest.Length;

            if (nameLength is 0 or > MaximumNameBytes || mediaTypeLength is 0 or > MaximumMediaTypeBytes)
                throw new PixelStegException("A payload bundle entry header is invalid.");
            if (contentLength > (ulong)PixelStegLimits.MaximumPayloadBytes || contentLength > int.MaxValue)
                throw new PixelStegException("A payload bundle entry length is invalid.");
            var variableLength = checked(nameLength + mediaTypeLength + (int)contentLength);
            EnsureAvailable(encoded, offset, variableLength);

            string name;
            string mediaType;
            try
            {
                name = StrictUtf8.GetString(encoded.Slice(offset, nameLength));
                offset += nameLength;
                mediaType = StrictUtf8.GetString(encoded.Slice(offset, mediaTypeLength));
                offset += mediaTypeLength;
            }
            catch (DecoderFallbackException)
            {
                throw new PixelStegException("A payload bundle string is not valid UTF-8.");
            }

            var content = encoded.Slice(offset, checked((int)contentLength)).ToArray();
            offset += content.Length;
            if (!CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(content)))
                throw new PixelStegException("Payload bundle integrity validation failed.");

            var entry = new PayloadEntry(kind, name, mediaType, content);
            ValidateEntry(entry, names);
            entries.Add(entry);
        }

        if (offset != encoded.Length)
            throw new PixelStegException("The payload bundle contains trailing bytes.");
        return new PayloadBundle(entries);
    }

    private static void ValidateEntry(PayloadEntry entry, ISet<string> names)
    {
        if (entry.Kind is not PayloadKind.Message and not PayloadKind.File)
            throw new PixelStegException("A payload bundle entry kind is invalid.");
        if (string.IsNullOrWhiteSpace(entry.Name) || entry.Name is "." or ".." ||
            entry.Name.IndexOfAny(['/', '\\', ':', '\0']) >= 0 || entry.Name.Any(char.IsControl))
            throw new PixelStegException("A payload bundle entry name is not safe.");
        if (!names.Add(entry.Name))
            throw new PixelStegException("Payload bundle entry names must be unique.");
        if (string.IsNullOrWhiteSpace(entry.MediaType) || entry.MediaType.Any(char.IsControl))
            throw new PixelStegException("A payload bundle media type is invalid.");
        if (entry.Kind == PayloadKind.Message)
        {
            try { _ = StrictUtf8.GetString(entry.Content); }
            catch (DecoderFallbackException)
            {
                throw new PixelStegException("A message entry must contain valid UTF-8.");
            }
        }
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> source, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > source.Length - length)
            throw new PixelStegException("The payload bundle is truncated.");
    }

    private sealed record PreparedEntry(PayloadEntry Entry, byte[] Name, byte[] MediaType, byte[] Digest);
}
