using System.Text;
using PixelSteg.Core;

namespace PixelSteg.Core.Tests;

public sealed class ContainerCodecTests
{
    [Fact]
    public async Task AtomicFileWriter_PreservesExistingDestination_WhenWriteFails()
    {
        var directory = CreateDirectory();
        var destination = Path.Combine(directory, "existing.bin");
        await File.WriteAllTextAsync(destination, "original");
        try
        {
            await Assert.ThrowsAsync<IOException>(() => AtomicFileWriter.WriteAsync(destination, overwrite: true, async (stream, token) =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("partial"), token);
                throw new IOException("simulated failure");
            }, CancellationToken.None));

            Assert.Equal("original", await File.ReadAllTextAsync(destination));
            Assert.Empty(Directory.GetFiles(directory, "*.partial"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task AtomicFileWriter_LeavesNoDestination_WhenWriteIsCancelled()
    {
        var directory = CreateDirectory();
        var destination = Path.Combine(directory, "new.bin");
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFileWriter.WriteAsync(destination, overwrite: false, (_, _) => throw new OperationCanceledException(), CancellationToken.None));
            Assert.False(File.Exists(destination));
            Assert.Empty(Directory.GetFiles(directory, "*.partial"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    [Fact]
    public async Task PackAndUnpack_RetainsVersionFilenamePayloadAndDigest()
    {
        var source = Encoding.UTF8.GetBytes("A safe, inert fixture.");
        await using var input = new MemoryStream(source);
        await using var container = new MemoryStream();

        await ContainerCodec.PackAsync(input, "café.txt", container, CancellationToken.None);
        var bytes = container.ToArray();

        Assert.Equal("PSTG", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(1u, BitConverter.ToUInt32(bytes, 4));

        container.Position = 0;
        await using var output = new MemoryStream();
        var header = await ContainerCodec.UnpackAsync(container, output, CancellationToken.None);

        Assert.Equal(1u, header.Version);
        Assert.Equal("café.txt", header.FileName);
        Assert.Equal(source.LongLength, header.PayloadLength);
        Assert.Equal(source, output.ToArray());
        Assert.Equal(32, header.Sha256.Length);
    }

    [Fact]
    public async Task Unpack_RejectsPayloadWhoseDigestDoesNotMatch()
    {
        await using var input = new MemoryStream(new byte[] { 1, 2, 3 });
        await using var container = new MemoryStream();
        await ContainerCodec.PackAsync(input, "data.bin", container, CancellationToken.None);
        var corrupt = container.ToArray();
        corrupt[^1] ^= 0xFF;

        await using var output = new MemoryStream();
        await Assert.ThrowsAsync<PixelStegException>(() => ContainerCodec.UnpackAsync(new MemoryStream(corrupt), output, CancellationToken.None));
        Assert.Empty(output.ToArray());
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PixelStegTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
