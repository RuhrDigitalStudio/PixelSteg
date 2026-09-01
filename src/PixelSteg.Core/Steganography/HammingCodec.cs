namespace PixelSteg.Core;

public readonly record struct HammingDecodeResult(byte Value, bool Corrected);

public static class HammingCodec
{
    private static readonly int[] DataPositions = [3, 5, 6, 7, 9, 10, 11, 12];
    private static readonly int[] ParityPositions = [1, 2, 4, 8];

    public static ushort Encode(byte value)
    {
        ushort codeword = 0;
        for (var index = 0; index < DataPositions.Length; index++)
        {
            var bit = (value >> (7 - index)) & 1;
            if (bit != 0) codeword |= (ushort)(1 << (DataPositions[index] - 1));
        }

        foreach (var parityPosition in ParityPositions)
        {
            var parity = 0;
            for (var position = 1; position <= 12; position++)
            {
                if (position != parityPosition && (position & parityPosition) != 0)
                    parity ^= (codeword >> (position - 1)) & 1;
            }
            if (parity != 0) codeword |= (ushort)(1 << (parityPosition - 1));
        }
        return codeword;
    }

    public static HammingDecodeResult Decode(ushort encoded)
    {
        encoded &= 0x0fff;
        var syndrome = 0;
        foreach (var parityPosition in ParityPositions)
        {
            var parity = 0;
            for (var position = 1; position <= 12; position++)
            {
                if ((position & parityPosition) != 0)
                    parity ^= (encoded >> (position - 1)) & 1;
            }
            if (parity != 0) syndrome |= parityPosition;
        }

        var corrected = syndrome is >= 1 and <= 12;
        if (corrected) encoded ^= (ushort)(1 << (syndrome - 1));

        byte value = 0;
        foreach (var position in DataPositions)
            value = (byte)((value << 1) | ((encoded >> (position - 1)) & 1));
        return new HammingDecodeResult(value, corrected);
    }
}
