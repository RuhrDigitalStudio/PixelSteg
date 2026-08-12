namespace PixelSteg.Core;

public static class PixelStegLimits
{
    public const int MaximumFileNameBytes = 1024;
    public const long MaximumPayloadBytes = 128L * 1024 * 1024;
    public const long MaximumContainerBytes = MaximumPayloadBytes + MaximumFileNameBytes + 64;
    public const int MaximumPixels = (int)((MaximumContainerBytes + 10) / 3);
    public const long MaximumPngRawBytes = MaximumContainerBytes + 64 * 1024;
    public const long MaximumPngDataBytes = MaximumPngRawBytes + 2 * 1024 * 1024;
}
