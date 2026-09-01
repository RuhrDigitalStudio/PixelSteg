using System.Buffers.Binary;
using System.IO.Compression;

namespace PixelSteg.Core;

public static class PngCodec
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] HeaderChunk = "IHDR"u8.ToArray();
    private static readonly byte[] DataChunk = "IDAT"u8.ToArray();
    private static readonly byte[] EndChunk = "IEND"u8.ToArray();

    public static async Task<PngImage> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!(await ReadExactlyAsync(input, Signature.Length, cancellationToken)).AsSpan().SequenceEqual(Signature))
            throw new PixelStegException("This is not a PNG image.");

        var width = 0;
        var height = 0;
        var channels = 0;
        var haveHeader = false;
        var haveData = false;
        var haveEnd = false;
        await using var compressed = new MemoryStream();

        while (!haveEnd)
        {
            var lengthBytes = await ReadExactlyAsync(input, 4, cancellationToken);
            var encodedLength = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
            if (encodedLength > PixelStegLimits.MaximumCoverCompressedBytes)
                throw new PixelStegException("The PNG chunk is too large.");

            var type = await ReadExactlyAsync(input, 4, cancellationToken);
            var data = await ReadExactlyAsync(input, checked((int)encodedLength), cancellationToken);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(await ReadExactlyAsync(input, 4, cancellationToken));
            if (PngCrc32.Compute(type, data) != expectedCrc)
                throw new PixelStegException("PNG integrity check failed.");

            if (type.AsSpan().SequenceEqual(HeaderChunk))
            {
                if (haveHeader || haveData || data.Length != 13)
                    throw new PixelStegException("The PNG header is invalid.");

                var encodedWidth = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4));
                var encodedHeight = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4));
                if (encodedWidth is 0 or > int.MaxValue || encodedHeight is 0 or > int.MaxValue)
                    throw new PixelStegException("The PNG dimensions are invalid.");

                width = (int)encodedWidth;
                height = (int)encodedHeight;
                if ((long)width * height > PixelStegLimits.MaximumCoverPixels)
                    throw new PixelStegException("The PNG dimensions exceed the safety limit.");
                if (data[8] != 8 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    throw new PixelStegException("Only non-interlaced 8-bit RGB and RGBA PNG files are supported.");

                channels = data[9] switch
                {
                    2 => 3,
                    6 => 4,
                    _ => throw new PixelStegException("Only non-interlaced 8-bit RGB and RGBA PNG files are supported.")
                };
                haveHeader = true;
            }
            else if (type.AsSpan().SequenceEqual(DataChunk))
            {
                if (!haveHeader) throw new PixelStegException("PNG data precedes its header.");
                if (compressed.Length > PixelStegLimits.MaximumCoverCompressedBytes - data.Length)
                    throw new PixelStegException("The PNG image data exceeds the safety limit.");
                await compressed.WriteAsync(data, cancellationToken);
                haveData = true;
            }
            else if (type.AsSpan().SequenceEqual(EndChunk))
            {
                if (!haveHeader || !haveData || data.Length != 0)
                    throw new PixelStegException("The PNG end chunk is invalid.");
                haveEnd = true;
            }
            else if (IsCritical(type))
            {
                throw new PixelStegException($"Unsupported critical PNG chunk '{System.Text.Encoding.ASCII.GetString(type)}'.");
            }
        }

        if (await input.ReadAsync(new byte[1], cancellationToken) != 0)
            throw new PixelStegException("The PNG has trailing data.");

        var rowBytes = checked(width * channels);
        var rawLength = checked((long)height * (rowBytes + 1L));
        if (rawLength > PixelStegLimits.MaximumCoverRawBytes)
            throw new PixelStegException("The decoded PNG exceeds the safety limit.");

        var raw = new byte[checked((int)rawLength)];
        try
        {
            compressed.Position = 0;
            await using var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            await ReadExactlyIntoAsync(zlib, raw, cancellationToken);
            if (await zlib.ReadAsync(new byte[1], cancellationToken) != 0)
                throw new PixelStegException("PNG data is longer than its dimensions.");
        }
        catch (InvalidDataException ex)
        {
            throw new PixelStegException($"PNG decompression failed: {ex.Message}");
        }

        var scanlines = new byte[checked(width * height * channels)];
        for (var row = 0; row < height; row++)
        {
            var rawOffset = row * (rowBytes + 1);
            var filter = raw[rawOffset];
            if (filter > 4) throw new PixelStegException("The PNG uses an invalid row filter.");

            var targetOffset = row * rowBytes;
            var previousOffset = targetOffset - rowBytes;
            for (var column = 0; column < rowBytes; column++)
            {
                var filtered = raw[rawOffset + 1 + column];
                var left = column >= channels ? scanlines[targetOffset + column - channels] : (byte)0;
                var above = row > 0 ? scanlines[previousOffset + column] : (byte)0;
                var upperLeft = row > 0 && column >= channels
                    ? scanlines[previousOffset + column - channels]
                    : (byte)0;
                var predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => above,
                    3 => (left + above) / 2,
                    4 => Paeth(left, above, upperLeft),
                    _ => 0
                };
                scanlines[targetOffset + column] = unchecked((byte)(filtered + predictor));
            }
        }

        var pixels = new byte[checked(width * height * 4)];
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = row * rowBytes;
            var pixelOffset = row * width * 4;
            for (var column = 0; column < width; column++)
            {
                pixels[pixelOffset++] = scanlines[sourceOffset++];
                pixels[pixelOffset++] = scanlines[sourceOffset++];
                pixels[pixelOffset++] = scanlines[sourceOffset++];
                pixels[pixelOffset++] = channels == 4 ? scanlines[sourceOffset++] : byte.MaxValue;
            }
        }

        return new PngImage(width, height, channels == 4, pixels);
    }

    public static async Task WriteAsync(PngImage image, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(output);
        var channels = image.HasAlpha ? 4 : 3;
        var rowBytes = checked(image.Width * channels);
        var raw = new byte[checked(image.Height * (rowBytes + 1))];

        for (var row = 0; row < image.Height; row++)
        {
            var rawOffset = row * (rowBytes + 1);
            raw[rawOffset] = 0;
            var target = rawOffset + 1;
            var source = row * image.Width * 4;
            for (var column = 0; column < image.Width; column++)
            {
                raw[target++] = image.Pixels[source++];
                raw[target++] = image.Pixels[source++];
                raw[target++] = image.Pixels[source++];
                var alpha = image.Pixels[source++];
                if (image.HasAlpha) raw[target++] = alpha;
            }
        }

        await output.WriteAsync(Signature, cancellationToken);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), checked((uint)image.Width));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), checked((uint)image.Height));
        header[8] = 8;
        header[9] = image.HasAlpha ? (byte)6 : (byte)2;
        await WriteChunkAsync(output, HeaderChunk, header, cancellationToken);

        await using var compressed = new MemoryStream();
        await using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            await zlib.WriteAsync(raw, cancellationToken);
        await WriteChunkAsync(output, DataChunk, compressed.ToArray(), cancellationToken);
        await WriteChunkAsync(output, EndChunk, [], cancellationToken);
    }

    private static bool IsCritical(ReadOnlySpan<byte> type) => (type[0] & 0x20) == 0;

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        var estimate = left + above - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var aboveDistance = Math.Abs(estimate - above);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);
        if (leftDistance <= aboveDistance && leftDistance <= upperLeftDistance) return left;
        return aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    private static async Task WriteChunkAsync(Stream output, byte[] type, byte[] data, CancellationToken cancellationToken)
    {
        var number = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)data.Length));
        await output.WriteAsync(number, cancellationToken);
        await output.WriteAsync(type, cancellationToken);
        await output.WriteAsync(data, cancellationToken);
        BinaryPrimitives.WriteUInt32BigEndian(number, PngCrc32.Compute(type, data));
        await output.WriteAsync(number, cancellationToken);
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream input, int length, CancellationToken cancellationToken)
    {
        var value = new byte[length];
        await ReadExactlyIntoAsync(input, value, cancellationToken);
        return value;
    }

    private static async Task ReadExactlyIntoAsync(Stream input, byte[] value, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < value.Length)
        {
            var read = await input.ReadAsync(value.AsMemory(offset), cancellationToken);
            if (read == 0) throw new PixelStegException("The PNG is truncated.");
            offset += read;
        }
    }

    private static class PngCrc32
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256).Select(Create).ToArray();

        public static uint Compute(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            var crc = 0xffffffffu;
            foreach (var value in type) crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8);
            foreach (var value in data) crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8);
            return crc ^ 0xffffffffu;
        }

        private static uint Create(int value)
        {
            var crc = (uint)value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 1 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            return crc;
        }
    }
}
