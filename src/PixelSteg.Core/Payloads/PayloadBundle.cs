namespace PixelSteg.Core;

public enum PayloadKind : byte
{
    Message = 1,
    File = 2
}

public sealed class PayloadEntry
{
    public PayloadEntry(PayloadKind kind, string name, string mediaType, byte[] content)
    {
        Kind = kind;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public PayloadKind Kind { get; }

    public string Name { get; }

    public string MediaType { get; }

    public byte[] Content { get; }
}

public sealed class PayloadBundle
{
    public PayloadBundle(IReadOnlyList<PayloadEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries.ToArray();
    }

    public IReadOnlyList<PayloadEntry> Entries { get; }
}
