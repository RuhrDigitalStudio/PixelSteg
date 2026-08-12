namespace PixelSteg.Cli;

public static class FileSafety
{
    public static string SanitizeEmbeddedFileName(string embeddedName)
    {
        var name = Path.GetFileName(embeddedName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") return "decoded.bin";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    public static string ResolveOutputPath(string directory, string embeddedName)
    {
        var root = Path.GetFullPath(directory);
        var candidate = Path.GetFullPath(Path.Combine(root, SanitizeEmbeddedFileName(embeddedName)));
        if (!candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The decoded file name is not safe for the selected directory.");
        return candidate;
    }
}
