using System.Text;
using PixelSteg.Core;

namespace PixelSteg.Core.Tests;

public sealed class PixelCodecTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Unicode: \uD83D\uDE80 café")]
    public async Task EncodeAndDecode_RetainsEveryContainerByte(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        await using var png = new MemoryStream();
        await PixelCodec.EncodeAsync(new MemoryStream(payload), png, CancellationToken.None);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.ToArray()[..8]);

        png.Position = 0;
        await using var decoded = new MemoryStream();
        await PixelCodec.DecodeAsync(png, decoded, CancellationToken.None);
        Assert.Equal(payload, decoded.ToArray());
    }

    [Fact]
    public async Task Decode_RejectsTruncatedCorruptAndInvalidImages()
    {
        await Assert.ThrowsAsync<PixelStegException>(() => PixelCodec.DecodeAsync(new MemoryStream(new byte[] { 1, 2, 3 }), new MemoryStream(), CancellationToken.None));

        await using var png = new MemoryStream();
        await PixelCodec.EncodeAsync(new MemoryStream(new byte[] { 1, 2, 3 }), png, CancellationToken.None);
        var corrupt = png.ToArray();
        corrupt[^8] ^= 0xFF;
        await Assert.ThrowsAsync<PixelStegException>(() => PixelCodec.DecodeAsync(new MemoryStream(corrupt), new MemoryStream(), CancellationToken.None));
        await Assert.ThrowsAsync<PixelStegException>(() => PixelCodec.DecodeAsync(new MemoryStream(png.ToArray()[..20]), new MemoryStream(), CancellationToken.None));
    }
}
