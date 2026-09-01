using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PixelSteg.Core;

public static class StegoEnvelope
{
    private static readonly byte[] Magic = "PST2"u8.ToArray();
    private const byte Version = 2;
    private const byte CompressedFlag = 1;
    private const byte EncryptedFlag = 2;
    private const int Iterations = 600_000;
    private const int SaltOffset = 22;
    private const int NonceOffset = 38;
    private const int TagOffset = 50;
    private const int CrcOffset = 66;
    private const int AssociatedDataLength = TagOffset;

    public const int LocatorSize = 70;

    public static StegoFrame Protect(
        ReadOnlySpan<byte> payload,
        EmbeddingProfile profile,
        StegoProtection protection)
    {
        ArgumentNullException.ThrowIfNull(protection);
        ValidateProfile(profile);
        if (payload.Length > PixelStegLimits.MaximumBundleBytes)
            throw new PixelStegException("The payload exceeds the bundle size limit.");

        var plainBody = protection.Compress ? Compress(payload) : payload.ToArray();
        if (plainBody.LongLength > PixelStegLimits.MaximumStegoEnvelopeBytes)
            throw new PixelStegException("The protected payload exceeds the envelope size limit.");

        var encrypted = !string.IsNullOrEmpty(protection.Password);
        var flags = (byte)((protection.Compress ? CompressedFlag : 0) | (encrypted ? EncryptedFlag : 0));
        var locator = new byte[LocatorSize];
        Magic.CopyTo(locator, 0);
        locator[4] = Version;
        locator[5] = (byte)profile;
        locator[6] = flags;
        locator[7] = profile == EmbeddingProfile.Dense ? (byte)2 : (byte)1;
        BinaryPrimitives.WriteUInt16LittleEndian(locator.AsSpan(8, 2), LocatorSize);
        BinaryPrimitives.WriteUInt64LittleEndian(locator.AsSpan(10, 8), checked((ulong)plainBody.LongLength));
        BinaryPrimitives.WriteInt32LittleEndian(locator.AsSpan(18, 4), encrypted ? Iterations : 0);
        RandomNumberGenerator.Fill(locator.AsSpan(SaltOffset, 16));

        byte[] body;
        if (encrypted)
        {
            RandomNumberGenerator.Fill(locator.AsSpan(NonceOffset, 12));
            body = new byte[plainBody.Length];
            var key = DeriveKey(protection.Password!, locator.AsSpan(SaltOffset, 16));
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Encrypt(
                    locator.AsSpan(NonceOffset, 12),
                    plainBody,
                    body,
                    locator.AsSpan(TagOffset, 16),
                    locator.AsSpan(0, AssociatedDataLength));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plainBody);
            }
        }
        else
        {
            body = plainBody;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(locator.AsSpan(CrcOffset, 4), ComputeCrc32(locator.AsSpan(0, CrcOffset)));
        return new StegoFrame(locator, body, Inspect(locator));
    }

    public static byte[] Unprotect(
        ReadOnlySpan<byte> locator,
        ReadOnlySpan<byte> body,
        string? password)
    {
        var info = Inspect(locator);
        if (!info.IsPresent) throw new PixelStegException("No PixelSteg locator was found.");
        if (body.Length != info.EnvelopeLength)
            throw new PixelStegException("The PixelSteg envelope is truncated or has trailing data.");

        byte[] protectedBytes;
        if (info.IsEncrypted)
        {
            if (string.IsNullOrEmpty(password))
                throw new PixelStegException("The protected payload could not be authenticated.");
            protectedBytes = new byte[body.Length];
            var key = DeriveKey(password, locator.Slice(SaltOffset, 16));
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(
                    locator.Slice(NonceOffset, 12),
                    body,
                    locator.Slice(TagOffset, 16),
                    protectedBytes,
                    locator.Slice(0, AssociatedDataLength));
            }
            catch (CryptographicException)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
                throw new PixelStegException("The protected payload could not be authenticated.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        else
        {
            protectedBytes = body.ToArray();
        }

        if (!info.IsCompressed) return protectedBytes;
        try
        {
            var decompressed = Decompress(protectedBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);
            return decompressed;
        }
        catch (InvalidDataException)
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            throw new PixelStegException("The compressed payload is invalid.");
        }
    }

    public static StegoFrameInfo Inspect(ReadOnlySpan<byte> locator)
    {
        if (locator.Length < Magic.Length || !locator[..Magic.Length].SequenceEqual(Magic))
            return new StegoFrameInfo(false, null, false, false, 0);
        if (locator.Length < LocatorSize)
            throw new PixelStegException("The PixelSteg locator is truncated.");
        if (locator[4] != Version)
            throw new PixelStegException("This PixelSteg locator version is not supported.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(locator.Slice(8, 2)) != LocatorSize)
            throw new PixelStegException("The PixelSteg locator size is invalid.");

        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(locator.Slice(CrcOffset, 4));
        if (expectedCrc != ComputeCrc32(locator.Slice(0, CrcOffset)))
            throw new PixelStegException("The PixelSteg locator integrity check failed.");

        var profile = (EmbeddingProfile)locator[5];
        ValidateProfile(profile);
        var expectedBits = profile == EmbeddingProfile.Dense ? 2 : 1;
        if (locator[7] != expectedBits)
            throw new PixelStegException("The PixelSteg profile settings are inconsistent.");

        var flags = locator[6];
        if ((flags & ~(CompressedFlag | EncryptedFlag)) != 0)
            throw new PixelStegException("The PixelSteg locator flags are invalid.");
        var encrypted = (flags & EncryptedFlag) != 0;
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(locator.Slice(18, 4));
        if (iterations != (encrypted ? Iterations : 0))
            throw new PixelStegException("The PixelSteg password parameters are invalid.");

        var bodyLength = BinaryPrimitives.ReadUInt64LittleEndian(locator.Slice(10, 8));
        if (bodyLength > (ulong)PixelStegLimits.MaximumStegoEnvelopeBytes)
            throw new PixelStegException("The PixelSteg envelope length is invalid.");

        return new StegoFrameInfo(
            true,
            profile,
            (flags & CompressedFlag) != 0,
            encrypted,
            checked((long)bodyLength));
    }

    private static byte[] Compress(ReadOnlySpan<byte> input)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            brotli.Write(input);
        return output.ToArray();
    }

    private static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        using var source = new MemoryStream(input.ToArray(), writable: false);
        using var brotli = new BrotliStream(source, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = brotli.Read(buffer);
            if (read == 0) break;
            if (output.Length > PixelStegLimits.MaximumBundleBytes - read)
                throw new InvalidDataException("The decompressed payload exceeds the bundle size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static void ValidateProfile(EmbeddingProfile profile)
    {
        if (profile is < EmbeddingProfile.Balanced or > EmbeddingProfile.Resilient)
            throw new PixelStegException("The embedding profile is not supported.");
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 1 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xffffffffu;
    }
}
