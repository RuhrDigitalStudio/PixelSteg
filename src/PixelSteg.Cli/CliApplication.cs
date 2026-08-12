using PixelSteg.Core;

namespace PixelSteg.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        try
        {
            if (args.Length == 3 && string.Equals(args[0], "encode", StringComparison.OrdinalIgnoreCase))
                return await EncodeCommand.RunAsync(args[1], args[2], error, cancellationToken);
            if (args.Length is 3 or 4 && string.Equals(args[0], "decode", StringComparison.OrdinalIgnoreCase) && (args.Length == 3 || args[3] == "--overwrite"))
                return await DecodeCommand.RunAsync(args[1], args[2], args.Length == 4, error, cancellationToken);
            await error.WriteLineAsync("Usage: pixelsteg encode <input> <output.png> | pixelsteg decode <input.png> <output-directory> [--overwrite]");
            return 2;
        }
        catch (OperationCanceledException) { await error.WriteLineAsync("Operation cancelled."); return 4; }
        catch (FileExistsException ex) { await error.WriteLineAsync(ex.Message); return 3; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PixelStegException or ArgumentException) { await error.WriteLineAsync($"Error: {ex.Message}"); return 1; }
    }
}

public sealed class FileExistsException(string message) : IOException(message);
