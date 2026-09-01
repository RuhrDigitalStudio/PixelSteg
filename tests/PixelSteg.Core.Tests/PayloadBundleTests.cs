using PixelSteg.Core;

namespace PixelSteg.Core.Tests;

public sealed class PayloadBundleTests
{
    [Fact]
    public void PackThenUnpack_PreservesMessagesAndFiles()
    {
        var bundle = new PayloadBundle(
        [
            new(PayloadKind.Message, "note.txt", "text/plain; charset=utf-8", "Treffen um 18 Uhr."u8.ToArray()),
            new(PayloadKind.File, "data.bin", "application/octet-stream", [0, 1, 2, 255])
        ]);

        var decoded = PayloadBundleCodec.Unpack(PayloadBundleCodec.Pack(bundle));

        Assert.Equal(2, decoded.Entries.Count);
        Assert.Equal(PayloadKind.Message, decoded.Entries[0].Kind);
        Assert.Equal("note.txt", decoded.Entries[0].Name);
        Assert.Equal("text/plain; charset=utf-8", decoded.Entries[0].MediaType);
        Assert.Equal("Treffen um 18 Uhr."u8.ToArray(), decoded.Entries[0].Content);
        Assert.Equal(PayloadKind.File, decoded.Entries[1].Kind);
        Assert.Equal("data.bin", decoded.Entries[1].Name);
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, decoded.Entries[1].Content);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("folder/file.txt")]
    [InlineData("folder\\file.txt")]
    [InlineData("..")]
    public void Pack_RejectsNamesThatCouldEscapeExtraction(string name)
    {
        var bundle = new PayloadBundle(
            [new(PayloadKind.File, name, "application/octet-stream", [1])]);

        Assert.Throws<PixelStegException>(() => PayloadBundleCodec.Pack(bundle));
    }

    [Fact]
    public void Unpack_RejectsChangedEntryContent()
    {
        var bundle = new PayloadBundle(
            [new(PayloadKind.File, "data.bin", "application/octet-stream", [1, 2, 3])]);
        var encoded = PayloadBundleCodec.Pack(bundle);
        encoded[^1] ^= 1;

        Assert.Throws<PixelStegException>(() => PayloadBundleCodec.Unpack(encoded));
    }
}
