using System.Buffers.Binary;
using System.IO.Compression;

namespace PixelSteg.Core;

public static class PixelCodec
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static async Task EncodeAsync(Stream container, Stream png, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(png);
        await using var source = new MemoryStream();
        await CopyToBoundedAsync(container, source, PixelStegLimits.MaximumContainerBytes, cancellationToken);
        var stored = new byte[checked((int)source.Length + 8)];
        BinaryPrimitives.WriteUInt64LittleEndian(stored, checked((ulong)source.Length));
        source.GetBuffer().AsSpan(0, checked((int)source.Length)).CopyTo(stored.AsSpan(8));
        var pixelCount = (stored.Length + 2) / 3;
        var width = checked((int)Math.Ceiling(Math.Sqrt(pixelCount)));
        var height = checked((int)Math.Ceiling((double)pixelCount / width));
        if ((long)width * height > PixelStegLimits.MaximumPixels) throw new PixelStegException("The PNG dimensions exceed the safety limit.");
        var raw = new byte[checked(height * (width * 3 + 1))];
        for (var row = 0; row < height; row++)
        {
            var target = row * (width * 3 + 1);
            raw[target] = 0;
            var copyFrom = row * width * 3;
            var copyCount = Math.Min(width * 3, stored.Length - copyFrom);
            if (copyCount > 0) stored.AsSpan(copyFrom, copyCount).CopyTo(raw.AsSpan(target + 1));
        }
        await png.WriteAsync(Signature, cancellationToken);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), checked((uint)height));
        ihdr[8] = 8; ihdr[9] = 2;
        await WriteChunkAsync(png, "IHDR"u8.ToArray(), ihdr, cancellationToken);
        await using var compressed = new MemoryStream();
        await using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) await zlib.WriteAsync(raw, cancellationToken);
        await WriteChunkAsync(png, "IDAT"u8.ToArray(), compressed.ToArray(), cancellationToken);
        await WriteChunkAsync(png, "IEND"u8.ToArray(), [], cancellationToken);
    }

    public static async Task DecodeAsync(Stream png, Stream container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(png);
        ArgumentNullException.ThrowIfNull(container);
        var signature = await ReadExactlyAsync(png, Signature.Length, cancellationToken);
        if (!signature.AsSpan().SequenceEqual(Signature)) throw new PixelStegException("This is not a PNG image.");
        int width = 0, height = 0; var haveHeader = false; var haveEnd = false;
        await using var idat = new MemoryStream();
        while (!haveEnd)
        {
            var lengthBytes = await ReadExactlyAsync(png, 4, cancellationToken);
            var length = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
            if (length > PixelStegLimits.MaximumPngDataBytes) throw new PixelStegException("The PNG chunk is too large.");
            var type = await ReadExactlyAsync(png, 4, cancellationToken);
            var data = await ReadExactlyAsync(png, checked((int)length), cancellationToken);
            var crc = await ReadExactlyAsync(png, 4, cancellationToken);
            var crcInput = new byte[4 + data.Length]; type.CopyTo(crcInput, 0); data.CopyTo(crcInput, 4);
            if (Crc32.Compute(crcInput) != BinaryPrimitives.ReadUInt32BigEndian(crc)) throw new PixelStegException("PNG integrity check failed.");
            if (type.AsSpan().SequenceEqual("IHDR"u8))
            {
                if (haveHeader || data.Length != 13) throw new PixelStegException("The PNG header is invalid.");
                var encodedWidth = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4));
                var encodedHeight = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4));
                if (encodedWidth > int.MaxValue || encodedHeight > int.MaxValue) throw new PixelStegException("The PNG dimensions are invalid.");
                width = (int)encodedWidth;
                height = (int)encodedHeight;
                if (width <= 0 || height <= 0 || (long)width * height > PixelStegLimits.MaximumPixels || data[8] != 8 || data[9] != 2 || data[10] != 0 || data[11] != 0 || data[12] != 0) throw new PixelStegException("Only non-interlaced 8-bit RGB PNG files are supported.");
                haveHeader = true;
            }
            else if (type.AsSpan().SequenceEqual("IDAT"u8))
            {
                if (!haveHeader) throw new PixelStegException("PNG data precedes its header.");
                if (idat.Length > PixelStegLimits.MaximumPngDataBytes - data.Length) throw new PixelStegException("The PNG image data exceeds the safety limit.");
                await idat.WriteAsync(data, cancellationToken);
            }
            else if (type.AsSpan().SequenceEqual("IEND"u8)) { if (!haveHeader || data.Length != 0) throw new PixelStegException("The PNG end chunk is invalid."); haveEnd = true; }
        }
        if (await png.ReadAsync(new byte[1], cancellationToken) != 0) throw new PixelStegException("The PNG has trailing data.");
        var rowBytes = checked(width * 3);
        var expectedRaw = checked((long)height * (rowBytes + 1));
        if (expectedRaw > PixelStegLimits.MaximumPngRawBytes) throw new PixelStegException("The PNG dimensions exceed the safety limit.");
        var raw = new byte[checked((int)expectedRaw)];
        try { idat.Position = 0; await using var zlib = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true); await ReadExactlyToBufferAsync(zlib, raw, cancellationToken); if (await zlib.ReadAsync(new byte[1], cancellationToken) != 0) throw new PixelStegException("PNG data is longer than its dimensions."); }
        catch (InvalidDataException ex) { throw new PixelStegException($"PNG decompression failed: {ex.Message}"); }
        var storedLength = checked((long)width * height * 3);
        if (storedLength > PixelStegLimits.MaximumContainerBytes + 10) throw new PixelStegException("The PNG dimensions exceed the safety limit.");
        var stored = new byte[checked((int)storedLength)];
        for (var row = 0; row < height; row++)
        {
            if (raw[row * (rowBytes + 1)] != 0) throw new PixelStegException("Only filter type 0 PNG files are supported.");
            raw.AsSpan(row * (rowBytes + 1) + 1, rowBytes).CopyTo(stored.AsSpan(row * rowBytes));
        }
        var byteCount = BinaryPrimitives.ReadUInt64LittleEndian(stored);
        if (byteCount > PixelStegLimits.MaximumContainerBytes || byteCount > (ulong)(stored.Length - 8)) throw new PixelStegException("The encoded byte count is invalid.");
        await container.WriteAsync(stored.AsMemory(8, checked((int)byteCount)), cancellationToken);
    }

    private static async Task WriteChunkAsync(Stream output, byte[] type, byte[] data, CancellationToken cancellationToken)
    {
        var length = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length)); await output.WriteAsync(length, cancellationToken); await output.WriteAsync(type, cancellationToken); await output.WriteAsync(data, cancellationToken);
        var crcInput = new byte[type.Length + data.Length]; type.CopyTo(crcInput, 0); data.CopyTo(crcInput, type.Length); BinaryPrimitives.WriteUInt32BigEndian(length, Crc32.Compute(crcInput)); await output.WriteAsync(length, cancellationToken);
    }
    private static async Task<byte[]> ReadExactlyAsync(Stream input, int length, CancellationToken cancellationToken) { var value = new byte[length]; await ReadExactlyToBufferAsync(input, value, cancellationToken); return value; }
    private static async Task ReadExactlyToBufferAsync(Stream input, byte[] value, CancellationToken cancellationToken) { var offset = 0; while (offset < value.Length) { var read = await input.ReadAsync(value.AsMemory(offset), cancellationToken); if (read == 0) throw new PixelStegException("The PNG is truncated."); offset += read; } }
    private static async Task CopyToBoundedAsync(Stream input, Stream output, long maximumLength, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) return;
            if (output.Length > maximumLength - read) throw new PixelStegException("The container exceeds the safety limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static class Crc32
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256).Select(Create).ToArray();
        public static uint Compute(ReadOnlySpan<byte> data) { uint crc = 0xffffffff; foreach (var value in data) crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8); return crc ^ 0xffffffff; }
        private static uint Create(int value) { uint crc = (uint)value; for (var bit = 0; bit < 8; bit++) crc = (crc & 1) == 1 ? 0xedb88320 ^ (crc >> 1) : crc >> 1; return crc; }
    }
}
