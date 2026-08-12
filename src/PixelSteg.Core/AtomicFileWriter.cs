namespace PixelSteg.Core;

public static class AtomicFileWriter
{
    public static async Task WriteAsync(string destinationPath, bool overwrite, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(write);
        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination) ?? throw new ArgumentException("The destination path is invalid.", nameof(destinationPath));
        var fileName = Path.GetFileName(destination);
        if (!overwrite && File.Exists(destination)) throw new IOException("The destination file already exists.");
        var temporary = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.partial");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await write(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
