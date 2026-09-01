using PixelSteg.Core;

namespace PixelSteg.Core.Tests;

public sealed class StegoEnvelopeTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "correct horse battery staple")]
    public void ProtectThenUnprotect_PreservesBundle(bool compress, string? password)
    {
        var source = PayloadBundleCodec.Pack(new PayloadBundle(
            [new(PayloadKind.Message, "note.txt", "text/plain", "hello"u8.ToArray())]));

        var frame = StegoEnvelope.Protect(
            source,
            EmbeddingProfile.Adaptive,
            new StegoProtection(compress, password));
        var decoded = StegoEnvelope.Unprotect(frame.Locator, frame.Body, password);

        Assert.Equal(source, decoded);
        Assert.True(frame.Info.IsPresent);
        Assert.Equal(EmbeddingProfile.Adaptive, frame.Info.Profile);
        Assert.Equal(compress, frame.Info.IsCompressed);
        Assert.Equal(!string.IsNullOrEmpty(password), frame.Info.IsEncrypted);
        Assert.Equal(frame.Body.LongLength, frame.Info.EnvelopeLength);
        Assert.Equal(70, frame.Locator.Length);
    }

    [Fact]
    public void Inspect_RecognizesProfileWithoutOpeningTheBody()
    {
        var frame = StegoEnvelope.Protect(
            "bundle bytes"u8,
            EmbeddingProfile.Dense,
            new StegoProtection(false, null));

        var info = StegoEnvelope.Inspect(frame.Locator);

        Assert.True(info.IsPresent);
        Assert.Equal(EmbeddingProfile.Dense, info.Profile);
        Assert.False(info.IsEncrypted);
        Assert.Equal(frame.Body.LongLength, info.EnvelopeLength);
    }

    [Fact]
    public void Unprotect_UsesTheSameNeutralErrorForWrongPasswordAndTampering()
    {
        var frame = StegoEnvelope.Protect(
            "authenticated bundle"u8,
            EmbeddingProfile.Resilient,
            new StegoProtection(true, "right password"));
        var changed = (byte[])frame.Body.Clone();
        changed[^1] ^= 1;

        var wrongPassword = Assert.Throws<PixelStegException>(
            () => StegoEnvelope.Unprotect(frame.Locator, frame.Body, "wrong password"));
        var tampered = Assert.Throws<PixelStegException>(
            () => StegoEnvelope.Unprotect(frame.Locator, changed, "right password"));

        Assert.Equal(wrongPassword.Message, tampered.Message);
    }

    [Fact]
    public void Inspect_RejectsLocatorCrcDamage()
    {
        var frame = StegoEnvelope.Protect(
            "bundle bytes"u8,
            EmbeddingProfile.Balanced,
            new StegoProtection(false, null));
        frame.Locator[12] ^= 1;

        Assert.Throws<PixelStegException>(() => StegoEnvelope.Inspect(frame.Locator));
    }
}
