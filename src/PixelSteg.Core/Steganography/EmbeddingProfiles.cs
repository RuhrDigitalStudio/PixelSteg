using System.Buffers.Binary;

namespace PixelSteg.Core;

internal static class EmbeddingProfiles
{
    private const int BlockSize = 8;

    public static IEnumerable<int> StableChannels(PngImage image)
    {
        for (var pixel = 0; pixel < image.Width * image.Height; pixel++)
        {
            var offset = pixel * 4;
            if (image.Pixels[offset + 3] != byte.MaxValue) continue;
            yield return offset;
            yield return offset + 1;
            yield return offset + 2;
        }
    }

    public static IEnumerable<int> BodyChannels(
        PngImage image,
        EmbeddingProfile profile,
        int locatorChannels,
        ReadOnlySpan<byte> salt)
    {
        if (profile is EmbeddingProfile.Balanced or EmbeddingProfile.Dense)
            return StableChannels(image).Skip(locatorChannels);

        var locator = StableChannels(image).Take(locatorChannels).ToHashSet();
        var blocks = DescribeBlocks(image, salt);
        return EnumerateBlocks(image, blocks, locator);
    }

    private static List<Block> DescribeBlocks(PngImage image, ReadOnlySpan<byte> salt)
    {
        var seed = BinaryPrimitives.ReadUInt64LittleEndian(salt[..8]);
        var blocks = new List<Block>();
        var blockIndex = 0;
        for (var top = 0; top < image.Height; top += BlockSize)
        {
            for (var left = 0; left < image.Width; left += BlockSize)
            {
                long sum = 0;
                long squareSum = 0;
                var count = 0;
                var bottom = Math.Min(top + BlockSize, image.Height);
                var right = Math.Min(left + BlockSize, image.Width);
                for (var y = top; y < bottom; y++)
                {
                    for (var x = left; x < right; x++)
                    {
                        var offset = (y * image.Width + x) * 4;
                        if (image.Pixels[offset + 3] != byte.MaxValue) continue;
                        var red = image.Pixels[offset] & 0xfc;
                        var green = image.Pixels[offset + 1] & 0xfc;
                        var blue = image.Pixels[offset + 2] & 0xfc;
                        var luminance = (54 * red + 183 * green + 19 * blue) >> 8;
                        sum += luminance;
                        squareSum += luminance * luminance;
                        count++;
                    }
                }

                if (count > 0)
                {
                    var variance = (double)(count * squareSum - sum * sum) / (count * count);
                    blocks.Add(new Block(left, top, variance, Mix(seed ^ (uint)blockIndex)));
                }
                blockIndex++;
            }
        }

        blocks.Sort(static (left, right) =>
        {
            var byVariance = right.Variance.CompareTo(left.Variance);
            return byVariance != 0 ? byVariance : left.TieBreaker.CompareTo(right.TieBreaker);
        });
        return blocks;
    }

    private static IEnumerable<int> EnumerateBlocks(
        PngImage image,
        IEnumerable<Block> blocks,
        ISet<int> locator)
    {
        foreach (var block in blocks)
        {
            var bottom = Math.Min(block.Top + BlockSize, image.Height);
            var right = Math.Min(block.Left + BlockSize, image.Width);
            for (var y = block.Top; y < bottom; y++)
            {
                for (var x = block.Left; x < right; x++)
                {
                    var offset = (y * image.Width + x) * 4;
                    if (image.Pixels[offset + 3] != byte.MaxValue) continue;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var index = offset + channel;
                        if (!locator.Contains(index)) yield return index;
                    }
                }
            }
        }
    }

    // SplitMix64 is a deterministic ordering primitive here, not a source of
    // cryptographic secrecy. Confidentiality comes only from AES-GCM.
    private static ulong Mix(ulong value)
    {
        value += 0x9e3779b97f4a7c15;
        value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9;
        value = (value ^ (value >> 27)) * 0x94d049bb133111eb;
        return value ^ (value >> 31);
    }

    private sealed record Block(int Left, int Top, double Variance, ulong TieBreaker);
}
