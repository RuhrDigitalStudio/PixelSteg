using PixelSteg.Core;

namespace PixelSteg.Core.Tests;

public sealed class StegoCodecTests
{
    [Theory]
    [InlineData(EmbeddingProfile.Balanced, 1)]
    [InlineData(EmbeddingProfile.Dense, 3)]
    [InlineData(EmbeddingProfile.Adaptive, 1)]
    [InlineData(EmbeddingProfile.Resilient, 1)]
    public void EmbedThenExtract_AutoDetectsEveryProfile(
        EmbeddingProfile profile,
        int maximumDelta)
    {
        var cover = CreateTexturedCover(100, 100, hasAlpha: false);
        var payload = "PixelSteg profile fixture"u8.ToArray();

        var embedded = StegoCodec.Embed(
            cover,
            payload,
            profile,
            new StegoProtection(false, null));
        var inspected = StegoCodec.Inspect(embedded.Image);
        var extracted = StegoCodec.Extract(embedded.Image, null);

        Assert.True(inspected.IsPresent);
        Assert.Equal(profile, inspected.Profile);
        Assert.Equal(profile, extracted.Frame.Profile);
        Assert.Equal(payload, extracted.Payload);
        Assert.InRange(embedded.Quality.MaximumChannelDelta, 0, maximumDelta);
        Assert.True(embedded.Quality.Psnr > 40);
    }

    [Fact]
    public void Embed_NeverUsesRgbHiddenUnderTransparency()
    {
        var cover = CreateTexturedCover(100, 100, hasAlpha: true);
        for (var pixel = 0; pixel < cover.Width * cover.Height; pixel += 3)
            cover.Pixels[pixel * 4 + 3] = 128;

        var embedded = StegoCodec.Embed(
            cover,
            "opaque channels only"u8,
            EmbeddingProfile.Adaptive,
            new StegoProtection(false, null));

        for (var pixel = 0; pixel < cover.Width * cover.Height; pixel++)
        {
            if (cover.Pixels[pixel * 4 + 3] == 255) continue;
            Assert.Equal(
                cover.Pixels.AsSpan(pixel * 4, 4).ToArray(),
                embedded.Image.Pixels.AsSpan(pixel * 4, 4).ToArray());
        }
    }

    [Fact]
    public void Measure_ReportsProfileSpecificExactCapacities()
    {
        var cover = CreateTexturedCover(30, 30, hasAlpha: false);

        var capacities = StegoCodec.Measure(cover).ToDictionary(item => item.Profile);

        Assert.Equal(267, capacities[EmbeddingProfile.Balanced].AvailablePayloadBytes);
        Assert.Equal(535, capacities[EmbeddingProfile.Dense].AvailablePayloadBytes);
        Assert.Equal(267, capacities[EmbeddingProfile.Adaptive].AvailablePayloadBytes);
        Assert.Equal(178, capacities[EmbeddingProfile.Resilient].AvailablePayloadBytes);
        Assert.All(capacities.Values, value => Assert.Equal(560, value.LocatorChannels));
    }

    [Fact]
    public void Inspect_ReturnsNoFrameForOrdinaryPixels()
    {
        var cover = new PngImage(20, 20, false, Enumerable.Repeat(
            new byte[] { 120, 120, 120, 255 }, 400).SelectMany(pixel => pixel).ToArray());

        var info = StegoCodec.Inspect(cover);

        Assert.False(info.IsPresent);
        Assert.Null(info.Profile);
    }

    [Fact]
    public void Embed_RejectsBodyBeyondMeasuredCapacity()
    {
        var cover = CreateTexturedCover(30, 30, hasAlpha: false);
        var capacity = StegoCodec.Measure(cover)
            .Single(item => item.Profile == EmbeddingProfile.Balanced)
            .AvailablePayloadBytes;

        Assert.Throws<PixelStegException>(() => StegoCodec.Embed(
            cover,
            new byte[capacity + 1],
            EmbeddingProfile.Balanced,
            new StegoProtection(false, null)));
    }

    private static PngImage CreateTexturedCover(int width, int height, bool hasAlpha)
    {
        var pixels = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var x = pixel % width;
            var y = pixel / width;
            pixels[pixel * 4] = (byte)((x * 17 + y * 29) & 255);
            pixels[pixel * 4 + 1] = (byte)((x * 43 + y * 11) & 255);
            pixels[pixel * 4 + 2] = (byte)((x * 7 + y * 61) & 255);
            pixels[pixel * 4 + 3] = 255;
        }
        return new PngImage(width, height, hasAlpha, pixels);
    }
}
