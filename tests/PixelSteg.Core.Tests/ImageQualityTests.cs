using PixelSteg.Core;

namespace PixelSteg.Core.Tests;

public sealed class ImageQualityTests
{
    [Fact]
    public void Compare_ReportsLiteralChannelDifference()
    {
        var cover = new PngImage(2, 1, false, [10, 20, 30, 255, 40, 50, 60, 255]);
        var result = new PngImage(2, 1, false, [11, 20, 30, 255, 40, 50, 60, 255]);

        var report = ImageQuality.Compare(cover, result, usedChannels: 4);

        Assert.Equal(1, report.ChangedChannels);
        Assert.Equal(1, report.MaximumChannelDelta);
        Assert.Equal(1d / 6d, report.MeanSquaredError, 12);
        Assert.InRange(report.Psnr, 55.91, 55.92);
        Assert.InRange(report.Ssim, 0.999, 1.0);
        Assert.Equal(4d / 6d, report.UsedCapacityRatio, 12);
    }

    [Fact]
    public void Compare_ReportsInfinitePsnrForIdenticalPixels()
    {
        var image = new PngImage(1, 1, false, [10, 20, 30, 255]);

        var report = ImageQuality.Compare(image, image.Clone(), usedChannels: 0);

        Assert.True(double.IsPositiveInfinity(report.Psnr));
        Assert.Equal(1, report.Ssim, 12);
    }
}
