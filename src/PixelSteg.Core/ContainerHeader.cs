namespace PixelSteg.Core;

public sealed record ContainerHeader(uint Version, string FileName, long PayloadLength, byte[] Sha256);
