namespace PixelSteg.Core;

public static class ImageQuality
{
    public static ImageQualityReport Compare(PngImage cover, PngImage result, long usedChannels)
    {
        ArgumentNullException.ThrowIfNull(cover);
        ArgumentNullException.ThrowIfNull(result);
        if (cover.Width != result.Width || cover.Height != result.Height || cover.Pixels.Length != result.Pixels.Length)
            throw new ArgumentException("Images must have matching dimensions.", nameof(result));

        long changed = 0;
        var maximumDelta = 0;
        double squaredError = 0;
        double coverMean = 0;
        double resultMean = 0;
        var pixelCount = cover.Width * cover.Height;

        for (var pixel = 0; pixel < pixelCount; pixel++)
        {
            var offset = pixel * 4;
            for (var channel = 0; channel < 3; channel++)
            {
                var delta = Math.Abs(cover.Pixels[offset + channel] - result.Pixels[offset + channel]);
                if (delta != 0) changed++;
                maximumDelta = Math.Max(maximumDelta, delta);
                squaredError += delta * delta;
            }
            coverMean += Luminance(cover.Pixels, offset);
            resultMean += Luminance(result.Pixels, offset);
        }

        coverMean /= pixelCount;
        resultMean /= pixelCount;
        double coverVariance = 0;
        double resultVariance = 0;
        double covariance = 0;
        for (var pixel = 0; pixel < pixelCount; pixel++)
        {
            var offset = pixel * 4;
            var coverDelta = Luminance(cover.Pixels, offset) - coverMean;
            var resultDelta = Luminance(result.Pixels, offset) - resultMean;
            coverVariance += coverDelta * coverDelta;
            resultVariance += resultDelta * resultDelta;
            covariance += coverDelta * resultDelta;
        }
        coverVariance /= pixelCount;
        resultVariance /= pixelCount;
        covariance /= pixelCount;

        var mse = squaredError / (pixelCount * 3d);
        var psnr = mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255d * 255d / mse);
        const double c1 = 6.5025;
        const double c2 = 58.5225;
        var ssim = ((2 * coverMean * resultMean + c1) * (2 * covariance + c2)) /
                   ((coverMean * coverMean + resultMean * resultMean + c1) *
                    (coverVariance + resultVariance + c2));
        var totalChannels = pixelCount * 3d;
        var usage = totalChannels == 0 ? 0 : Math.Clamp(usedChannels / totalChannels, 0, 1);
        return new ImageQualityReport(changed, maximumDelta, mse, psnr, ssim, usage);
    }

    private static double Luminance(byte[] pixels, int offset) =>
        0.2126 * pixels[offset] + 0.7152 * pixels[offset + 1] + 0.0722 * pixels[offset + 2];
}
