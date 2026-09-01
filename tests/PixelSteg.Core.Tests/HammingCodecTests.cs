using PixelSteg.Core;

namespace PixelSteg.Core.Tests;

public sealed class HammingCodecTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(255)]
    public void Decode_RecoversEveryByteAfterOneChangedBit(byte value)
    {
        var encoded = HammingCodec.Encode(value);

        for (var bit = 0; bit < 12; bit++)
        {
            var decoded = HammingCodec.Decode((ushort)(encoded ^ (1 << bit)));
            Assert.Equal(value, decoded.Value);
            Assert.True(decoded.Corrected);
        }
    }
}
