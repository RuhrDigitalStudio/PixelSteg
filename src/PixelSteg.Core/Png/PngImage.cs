namespace PixelSteg.Core;

public sealed class PngImage
{
    public PngImage(int width, int height, bool hasAlpha, byte[] pixels)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(pixels);

        var expectedLength = checked(width * height * 4);
        if (pixels.Length != expectedLength)
            throw new ArgumentException("Pixel data must contain one RGBA value per pixel.", nameof(pixels));

        if (!hasAlpha)
        {
            for (var offset = 3; offset < pixels.Length; offset += 4)
            {
                if (pixels[offset] != byte.MaxValue)
                    throw new ArgumentException("RGB images must use an opaque alpha value.", nameof(pixels));
            }
        }

        Width = width;
        Height = height;
        HasAlpha = hasAlpha;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public bool HasAlpha { get; }

    public byte[] Pixels { get; }

    public PngImage Clone() => new(Width, Height, HasAlpha, (byte[])Pixels.Clone());
}
