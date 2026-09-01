using PixelSteg.Core;
using System.Buffers.Binary;
using System.IO.Compression;

namespace PixelSteg.Core.Tests;

public sealed class PngCodecTests
{
    public static TheoryData<byte[]> FilteredRows => new()
    {
        new byte[] { 0, 10, 20, 30, 40, 50, 60, 0, 15, 30, 45, 55, 70, 85 },
        new byte[] { 1, 10, 20, 30, 30, 30, 30, 1, 15, 30, 45, 40, 40, 40 },
        new byte[] { 2, 10, 20, 30, 40, 50, 60, 2, 5, 10, 15, 15, 20, 25 },
        new byte[] { 3, 10, 20, 30, 35, 40, 45, 3, 10, 20, 30, 28, 30, 33 },
        new byte[] { 4, 10, 20, 30, 30, 30, 30, 4, 5, 10, 15, 15, 20, 25 }
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteThenRead_PreservesEveryPixel(bool hasAlpha)
    {
        var image = new PngImage(
            2,
            1,
            hasAlpha,
            [10, 20, 30, 255, 40, 50, 60, hasAlpha ? (byte)128 : (byte)255]);

        await using var encoded = new MemoryStream();
        await PngCodec.WriteAsync(image, encoded, CancellationToken.None);
        encoded.Position = 0;

        var decoded = await PngCodec.ReadAsync(encoded, CancellationToken.None);

        Assert.Equal(image.Width, decoded.Width);
        Assert.Equal(image.Height, decoded.Height);
        Assert.Equal(image.HasAlpha, decoded.HasAlpha);
        Assert.Equal(image.Pixels, decoded.Pixels);
    }

    [Theory]
    [MemberData(nameof(FilteredRows))]
    public async Task Read_ReversesEveryStandardFilter(byte[] filteredRows)
    {
        await using var png = BuildPng(2, 2, colorType: 2, interlace: 0, filteredRows);

        var decoded = await PngCodec.ReadAsync(png, CancellationToken.None);

        Assert.Equal(
            [10, 20, 30, 255, 40, 50, 60, 255, 15, 30, 45, 255, 55, 70, 85, 255],
            decoded.Pixels);
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(2, 1)]
    public async Task Read_RejectsUnsupportedColorModesAndInterlacing(byte colorType, byte interlace)
    {
        await using var png = BuildPng(1, 1, colorType, interlace, [0, 1, 2, 3]);

        await Assert.ThrowsAsync<PixelStegException>(
            () => PngCodec.ReadAsync(png, CancellationToken.None));
    }

    [Fact]
    public async Task Read_RejectsCrcDamageAndTruncation()
    {
        await using var valid = BuildPng(1, 1, colorType: 2, interlace: 0, [0, 1, 2, 3]);
        var bytes = valid.ToArray();
        bytes[^5] ^= 1;

        await Assert.ThrowsAsync<PixelStegException>(
            () => PngCodec.ReadAsync(new MemoryStream(bytes), CancellationToken.None));
        await Assert.ThrowsAsync<PixelStegException>(
            () => PngCodec.ReadAsync(new MemoryStream(bytes[..20]), CancellationToken.None));
    }

    private static MemoryStream BuildPng(
        int width,
        int height,
        byte colorType,
        byte interlace,
        byte[] filteredRows)
    {
        var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8;
        header[9] = colorType;
        header[12] = interlace;
        WriteChunk(output, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(filteredRows);
        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);
        output.Position = 0;
        return output;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ComputeCrc(type, data));
        output.Write(number);
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;
        foreach (var value in type) crc = Update(crc, value);
        foreach (var value in data) crc = Update(crc, value);
        return crc ^ 0xffffffffu;
    }

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc & 1) == 1 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }
}
